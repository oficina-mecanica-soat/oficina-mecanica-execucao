using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class ValidacaoContratoTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Theory]
    [InlineData("versao")]
    [InlineData("mensagemId")]
    [InlineData("sagaId")]
    [InlineData("ordemServicoId")]
    [InlineData("causationId")]
    [InlineData("correlationId")]
    [InlineData("ocorridoEm")]
    [InlineData("dados")]
    [InlineData("tipo")]
    public async Task Dado_EnvelopeInvalido_Quando_Processar_Entao_NaoPersisteEstado(string campo)
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados());
        comando = campo switch
        {
            "versao" => comando with { Versao = 2 },
            "mensagemId" => comando with { MensagemId = Guid.Empty },
            "sagaId" => comando with { SagaId = Guid.Empty },
            "ordemServicoId" => comando with { OrdemServicoId = Guid.Empty },
            "causationId" => comando with { CausationId = Guid.Empty },
            "correlationId" => comando with { CorrelationId = "" },
            "ocorridoEm" => comando with { OcorridoEm = default },
            "dados" => comando with { Dados = JsonSerializer.SerializeToElement("invalido") },
            _ => comando with { Tipo = "ComandoNaoSuportado" }
        };

        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(comando));

        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
    }

    [Theory]
    [InlineData("versao")]
    [InlineData("filialId")]
    [InlineData("veiculoId")]
    [InlineData("mecanicoId")]
    [InlineData("itensNulos")]
    [InlineData("servicosNulos")]
    [InlineData("itemNulo")]
    [InlineData("itemVazio")]
    [InlineData("quantidade")]
    [InlineData("repetido")]
    [InlineData("servicoNulo")]
    [InlineData("servicoVazio")]
    [InlineData("descricao")]
    [InlineData("valor")]
    [InlineData("limite")]
    public async Task Dado_PreparacaoInvalida_Quando_Processar_Entao_RejeitaSemConsultarEstoque(string campo)
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var dados = cenario.Dados();
        dados = campo switch
        {
            "versao" => dados with { Versao = 0 },
            "filialId" => dados with { FilialId = Guid.Empty },
            "veiculoId" => dados with { VeiculoId = Guid.Empty },
            "mecanicoId" => dados with { MecanicoId = Guid.Empty },
            "itensNulos" => dados with { Itens = null! },
            "servicosNulos" => dados with { Servicos = null! },
            "itemNulo" => dados with { Itens = [null!] },
            "itemVazio" => dados with { Itens = [new(Guid.Empty, 1)] },
            "quantidade" => dados with { Itens = [new(cenario.ItemId, 0)] },
            "repetido" => dados with { Itens = [new(cenario.ItemId, 1), new(cenario.ItemId, 1)] },
            "servicoNulo" => dados with { Servicos = [null!] },
            "servicoVazio" => dados with { Servicos = [new(Guid.Empty, "Troca", 1m)] },
            "descricao" => dados with { Servicos = [new(Guid.NewGuid(), "", 1m)] },
            "valor" => dados with { Servicos = [new(Guid.NewGuid(), "Troca", 0m)] },
            _ => dados with { Itens = Enumerable.Range(0, 94).Select(_ => new ItemSolicitado(Guid.NewGuid(), 1)).ToArray() }
        };

        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(cenario.Comando("PrepararExecucao", dados)));

        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"versao\":\"texto\"}")]
    [InlineData("{\"campoDesconhecido\":true}")]
    public async Task Dado_PayloadMalformado_Quando_Preparar_Entao_EnviaParaInvestigacao(string dadosJson)
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados()) with { Dados = JsonDocument.Parse(dadosJson).RootElement.Clone() };

        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(comando));
    }

    [Fact]
    public async Task Dado_CancelamentoSemIdOuMotivo_Quando_Processar_Entao_Rejeita()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(cenario.Comando("CancelarPreparacao", new CancelarPreparacao(Guid.Empty, "Recusa"))));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(cenario.Comando("CancelarPreparacao", new CancelarPreparacao(Guid.NewGuid(), ""))));
        var comando = cenario.Comando("CancelarPreparacao", new { preparacaoId = "nao-e-uuid" });
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(comando));
    }

    [Fact]
    public async Task Dado_VersaoInicialMaiorQueUm_Quando_Preparar_Entao_NaoCriaVersaoForaDeOrdem()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados(versao: 2))));
    }

    [Theory]
    [InlineData("https://dynamodb.us-east-1.amazonaws.com")]
    [InlineData("http://dynamodb.us-east-1.amazonaws.com")]
    public void Dado_EndpointRemoto_Quando_CriarModoLocal_Entao_RecusaAntesDeConectar(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => DynamoDbLocal.CriarClient(endpoint));
    }

    [Fact]
    public async Task Dado_TabelaExistente_Quando_InicializarNovamente_Entao_PreservaDados()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        using var client = fixture.CriarClient();

        await DynamoDbLocal.PrepararTabelaAsync(client, cenario.Tabela, default);

        Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
    }

    [Fact]
    public async Task Dado_TransacaoSemAcoesOuComChavesRepetidas_Quando_Confirmar_Entao_Rejeita()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.Armazenamento.ConfirmarAsync([], default));
        var registro = new Registro(new("TESTE", "A"), 0, null);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.Armazenamento.ConfirmarAsync([new(registro, true, "{}"), new(registro, true, "{}")], default));
        var tx = new TransacaoExecucao(cenario.Armazenamento);
        await tx.AdicionarAsync(registro.Chave, new { valor = 1 }, default);
        await tx.ConfirmarAsync(default);
        await Assert.ThrowsAsync<ConflitoConcorrenciaException>(() => new TransacaoExecucao(cenario.Armazenamento).AdicionarAsync(registro.Chave, new { valor = 2 }, default));
    }
}
