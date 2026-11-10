param([switch]$ManterContainers)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$diretorioMensagens = Join-Path $repo '.local/mensagens'
$execucaoId = [Guid]::NewGuid().ToString('N')
$diretorioValidacao = Join-Path $repo ('.local/validacao/' + $execucaoId)
$tabela = 'oficina-mecanica-execucao-local'
$salvos = @{}
foreach ($nome in @('AWS_ACCESS_KEY_ID', 'AWS_SECRET_ACCESS_KEY', 'AWS_SESSION_TOKEN')) {
    $salvos[$nome] = [Environment]::GetEnvironmentVariable($nome, 'Process')
}

function AwsLocal([string[]]$Parametros) {
    $resultado = & aws @Parametros --endpoint-url http://127.0.0.1:8000 --region us-east-1 --no-cli-pager 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Falha na operação DynamoDB Local; nenhum endpoint remoto é usado por este script.' }
    if ($resultado) { return ($resultado -join "`n" | ConvertFrom-Json) }
}

function Aguardar([scriptblock]$Condicao) {
    $limite = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $resultado = & $Condicao
        if ($resultado) { return $resultado }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $limite)
    throw 'A validação local não concluiu dentro do prazo.'
}

function GravarComando($Comando, [string]$Nome) {
    $arquivo = Join-Path $diretorioMensagens ('entrada/' + $Nome + '.json')
    $Comando | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath ($arquivo + '.tmp') -Encoding utf8NoBOM
    Move-Item -LiteralPath ($arquivo + '.tmp') -Destination $arquivo
}

function Resultado([Guid]$CausationId) {
    Get-ChildItem (Join-Path $diretorioMensagens 'saida') -Filter '*.json' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } |
        Where-Object { $_.causationId -eq $CausationId.ToString('D') } | Select-Object -First 1
}

Push-Location $repo
try {
    if (-not (Test-Path -LiteralPath '.env')) { throw 'Crie .env a partir de .env.example e defina JWT_SECRET antes de validar.' }
    New-Item -ItemType Directory -Path $diretorioValidacao, (Join-Path $diretorioMensagens 'entrada') -Force | Out-Null
    $env:AWS_ACCESS_KEY_ID = 'local'
    $env:AWS_SECRET_ACCESS_KEY = 'local'
    $env:AWS_SESSION_TOKEN = $null
    docker compose --env-file .env up -d --build
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar o Compose local.' }
    Aguardar {
        try { (AwsLocal @('dynamodb', 'describe-table', '--table-name', $tabela)).Table.TableStatus -eq 'ACTIVE' }
        catch { $false }
    } | Out-Null

    $filialId = [Guid]::NewGuid()
    $itemId = [Guid]::NewGuid()
    $osId = [Guid]::NewGuid()
    $conteudo = @{ id = [Guid]::NewGuid().ToString('D'); itemId = $itemId.ToString('D'); disponivel = 10; reservada = 0 } | ConvertTo-Json -Compress
    $item = @{ PK = @{ S = "ESTOQUE#$filialId" }; SK = @{ S = "ITEM#$itemId" }; revisao = @{ N = '1' }; conteudo = @{ S = $conteudo } }
    $arquivoSeed = Join-Path $diretorioValidacao 'saldo.json'
    $item | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $arquivoSeed -Encoding utf8NoBOM
    AwsLocal @('dynamodb', 'put-item', '--table-name', $tabela, '--item', ('file://' + $arquivoSeed), '--condition-expression', 'attribute_not_exists(PK)') | Out-Null
    $arquivoChave = Join-Path $diretorioValidacao 'chave.json'
    @{ PK = $item.PK; SK = $item.SK } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $arquivoChave -Encoding utf8NoBOM

    $preparar = @{
        mensagemId = [Guid]::NewGuid().ToString('D'); tipo = 'PrepararExecucao'; versao = 1
        sagaId = [Guid]::NewGuid().ToString('D'); ordemServicoId = $osId.ToString('D')
        correlationId = [Guid]::NewGuid().ToString('D'); causationId = [Guid]::NewGuid().ToString('D')
        ocorridoEm = [DateTime]::UtcNow.ToString('o')
        dados = @{ versao = 1; filialId = $filialId.ToString('D'); veiculoId = [Guid]::NewGuid().ToString('D')
            mecanicoId = [Guid]::NewGuid().ToString('D'); itens = @(@{ itemId = $itemId.ToString('D'); quantidade = 3 })
            servicos = @(@{ servicoId = [Guid]::NewGuid().ToString('D'); descricao = 'Troca de óleo'; valor = [decimal]125.50 }) }
    }
    GravarComando $preparar "$execucaoId-preparar"
    $preparada = Aguardar { Resultado ([Guid]$preparar.mensagemId) }
    if ($preparada.tipo -ne 'ExecucaoPreparada') { throw 'A preparação foi recusada.' }

    docker compose restart execucao | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao reiniciar o processo local.' }
    $preparar.mensagemId = [Guid]::NewGuid().ToString('D')
    GravarComando $preparar "$execucaoId-repetir-preparar"
    Aguardar { Test-Path -LiteralPath (Join-Path $diretorioMensagens "processadas/$execucaoId-repetir-preparar.json") } | Out-Null
    $saldo = (AwsLocal @('dynamodb', 'get-item', '--table-name', $tabela, '--key', ('file://' + $arquivoChave), '--consistent-read')).Item.conteudo.S | ConvertFrom-Json
    if ($saldo.disponivel -ne 7 -or $saldo.reservada -ne 3) { throw 'A reentrega alterou o saldo indevidamente.' }

    $cancelar = $preparar.Clone()
    $cancelar.mensagemId = [Guid]::NewGuid().ToString('D')
    $cancelar.tipo = 'CancelarPreparacao'
    $cancelar.causationId = $preparada.mensagemId
    $cancelar.dados = @{ preparacaoId = $preparada.dados.preparacaoId; motivo = 'Orçamento recusado na validação local' }
    GravarComando $cancelar "$execucaoId-cancelar"
    $cancelada = Aguardar { Resultado ([Guid]$cancelar.mensagemId) }
    if ($cancelada.tipo -ne 'PreparacaoCancelada') { throw 'A compensação não foi confirmada.' }
    docker compose restart execucao | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao reiniciar o processo local.' }
    $cancelar.mensagemId = [Guid]::NewGuid().ToString('D')
    GravarComando $cancelar "$execucaoId-repetir-cancelar"
    Aguardar { Test-Path -LiteralPath (Join-Path $diretorioMensagens "processadas/$execucaoId-repetir-cancelar.json") } | Out-Null
    $saldo = (AwsLocal @('dynamodb', 'get-item', '--table-name', $tabela, '--key', ('file://' + $arquivoChave), '--consistent-read')).Item.conteudo.S | ConvertFrom-Json
    if ($saldo.disponivel -ne 10 -or $saldo.reservada -ne 0) { throw 'O saldo final diverge do saldo inicial.' }
    Write-Output 'Validação local aprovada: reserva 10→7/3, reinício e reentrega sem nova reserva, cancelamento e reentrega com saldo final 10/0.'
    Write-Output ('Arquivos de evidência preservados em ' + $diretorioMensagens)
}
finally {
    if (-not $ManterContainers) { docker compose down | Out-Host }
    foreach ($nome in $salvos.Keys) { [Environment]::SetEnvironmentVariable($nome, $salvos[$nome], 'Process') }
    Pop-Location
}
