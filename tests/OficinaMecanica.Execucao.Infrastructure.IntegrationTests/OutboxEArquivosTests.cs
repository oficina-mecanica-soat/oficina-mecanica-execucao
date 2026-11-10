using OficinaMecanica.Execucao.Infrastructure.Persistencia;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class OutboxEArquivosTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Fact]
    public async Task Dado_CommitSemEnvio_Quando_ReiniciarPublicador_Entao_PublicaResultadoPersistido()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var transporte = new TransporteControlado();
        var publicador = new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio);

        Assert.True(await publicador.PublicarProximaAsync());
        Assert.False(await publicador.PublicarProximaAsync());

        Assert.Equal(preparada.MensagemId, Assert.Single(transporte.Enviadas).MensagemId);
        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
        Assert.Equal(7, (await cenario.SaldoAsync())!.Disponivel);
    }

    [Fact]
    public async Task Dado_FalhaNoTransporte_Quando_RetomarAposBackoff_Entao_ReenviaMesmoIdSemPerderEstado()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        var transporte = new TransporteControlado { FalhasRestantes = 1 };
        var publicador = new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio);
        var original = (await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default))!.Ler<PendenciaOutbox>()!;

        Assert.False(await publicador.PublicarProximaAsync());
        var pendencia = (await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default))!.Ler<PendenciaOutbox>()!;
        Assert.Equal(1, pendencia.Tentativas);
        Assert.Equal("FALHA_PUBLICACAO", pendencia.UltimoErro);
        Assert.False(await publicador.PublicarProximaAsync());
        cenario.Relogio.Avancar(TimeSpan.FromSeconds(3));
        Assert.True(await new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());
        Assert.Equal(original.Mensagem.MensagemId, Assert.Single(transporte.Enviadas).MensagemId);
    }

    [Fact]
    public async Task Dado_FalhaDepoisDoEnvio_Quando_LeaseExpirar_Entao_ReentregaMesmoResultado()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        var transporte = new TransporteControlado();
        var armazenamento = new ArmazenamentoComFalha(cenario.Armazenamento) { FalharConfirmacaoNumero = 2 };

        await Assert.ThrowsAsync<IOException>(() => new PublicadorOutbox(armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());
        Assert.Single(transporte.Enviadas);
        Assert.False(await new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());
        cenario.Relogio.Avancar(TimeSpan.FromMinutes(2));
        Assert.True(await new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());

        Assert.Equal(2, transporte.Enviadas.Count);
        Assert.Equal(transporte.Enviadas[0].MensagemId, transporte.Enviadas[1].MensagemId);
    }

    [Fact]
    public async Task Dado_OutboxConcorrente_Quando_DisputarLease_Entao_ConfirmaUmaPublicacao()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        var transporte = new TransporteControlado();
        await Task.WhenAll(new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio).PublicarProximaAsync(),
            new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());

        Assert.Single(transporte.Enviadas);
    }

    [Fact]
    public async Task Dado_PreparacaoECancelamentoNoMesmoInstante_Quando_Publicar_Entao_PreservaOrdemDeCommit()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        await cenario.CancelarAsync(preparada);
        var transporte = new TransporteControlado();
        var publicador = new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio);

        Assert.True(await publicador.PublicarProximaAsync());
        Assert.True(await publicador.PublicarProximaAsync());

        Assert.Equal(["ExecucaoPreparada", "PreparacaoCancelada"], transporte.Enviadas.Select(item => item.Tipo));
    }

    [Fact]
    public async Task Dado_ArquivoDuplicado_Quando_RetomarConsumidor_Entao_ArquivaAposCommitSemNovaReserva()
    {
        using var pasta = new PastaTemporaria();
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var entrada = Path.Combine(pasta.Caminho, "entrada");
        Directory.CreateDirectory(entrada);
        var mensagem = cenario.Comando("PrepararExecucao", cenario.Dados());
        await File.WriteAllTextAsync(Path.Combine(entrada, "01.json"), JsonContrato.Serializar(mensagem));
        var consumidor = new ConsumidorArquivos(cenario.Processador, pasta.Caminho);
        Assert.True(await consumidor.ProcessarProximaAsync());
        await File.WriteAllTextAsync(Path.Combine(entrada, "02.json"), JsonContrato.Serializar(mensagem));

        Assert.True(await new ConsumidorArquivos(CenarioPreparacao.CriarProcessador(cenario.Armazenamento, cenario.Relogio), pasta.Caminho).ProcessarProximaAsync());
        Assert.False(await consumidor.ProcessarProximaAsync());

        Assert.Equal(2, Directory.GetFiles(Path.Combine(pasta.Caminho, "processadas"), "*.json").Length);
        Assert.Equal(7, (await cenario.SaldoAsync())!.Disponivel);
        var publicador = new PublicadorOutbox(cenario.Armazenamento,
            new TransporteArquivos(Path.Combine(pasta.Caminho, "saida")), cenario.Relogio);
        Assert.True(await publicador.PublicarProximaAsync());
        Assert.Single(Directory.GetFiles(Path.Combine(pasta.Caminho, "saida"), "*.json"));
    }

    [Theory]
    [InlineData("{", "JSON_INVALIDO")]
    [InlineData("null", "JSON_NULO")]
    public async Task Dado_ArquivoInvalido_Quando_Consumir_Entao_EncaminhaParaInvestigacaoLocal(string json, string codigo)
    {
        using var pasta = new PastaTemporaria();
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        Directory.CreateDirectory(Path.Combine(pasta.Caminho, "entrada"));
        await File.WriteAllTextAsync(Path.Combine(pasta.Caminho, "entrada", "invalida.json"), json);

        Assert.True(await new ConsumidorArquivos(cenario.Processador, pasta.Caminho).ProcessarProximaAsync());

        Assert.Equal(codigo, await File.ReadAllTextAsync(Path.Combine(pasta.Caminho, "investigacao", "invalida.json.motivo.txt")));
        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
    }

    [Fact]
    public async Task Dado_FalhaAntesDoCommit_Quando_Consumir_Entao_MantemArquivoParaReentrega()
    {
        using var pasta = new PastaTemporaria();
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        Directory.CreateDirectory(Path.Combine(pasta.Caminho, "entrada"));
        var arquivo = Path.Combine(pasta.Caminho, "entrada", "pendente.json");
        await File.WriteAllTextAsync(arquivo, JsonContrato.Serializar(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] })));
        var armazenamento = new ArmazenamentoComFalha(cenario.Armazenamento) { FalharConfirmacaoNumero = 1 };

        await Assert.ThrowsAsync<IOException>(() => new ConsumidorArquivos(CenarioPreparacao.CriarProcessador(armazenamento, cenario.Relogio), pasta.Caminho).ProcessarProximaAsync());

        Assert.True(File.Exists(arquivo));
        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
        Assert.True(await new ConsumidorArquivos(cenario.Processador, pasta.Caminho).ProcessarProximaAsync());
    }

    [Fact]
    public async Task Dado_SaidaLocalDuplicadaOuDivergente_Quando_Publicar_Entao_DeduplicaOuRecusaConflito()
    {
        using var pasta = new PastaTemporaria();
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var mensagem = cenario.Comando("ExecucaoPreparada", new { versao = 1 });
        var transporte = new TransporteArquivos(pasta.Caminho);
        await transporte.PublicarAsync(mensagem, default);
        await transporte.PublicarAsync(mensagem, default);

        await Assert.ThrowsAsync<IOException>(() => transporte.PublicarAsync(mensagem with { Tipo = "PreparacaoCancelada" }, default));

        Assert.Single(Directory.GetFiles(pasta.Caminho, "*.json"));
        Assert.Empty(Directory.GetFiles(pasta.Caminho, "*.tmp"));
    }
}

internal sealed class TransporteControlado : ITransporteResultado
{
    public int FalhasRestantes { get; set; }
    public List<Mensagem> Enviadas { get; } = [];
    public Task PublicarAsync(Mensagem mensagem, CancellationToken cancellationToken)
    {
        if (FalhasRestantes-- > 0) throw new IOException("Detalhe técnico não deve persistir na outbox.");
        lock (Enviadas) Enviadas.Add(mensagem);
        return Task.CompletedTask;
    }
}

internal sealed class ArmazenamentoComFalha(IArmazenamentoExecucao inner) : IArmazenamentoExecucao
{
    public int FalharConfirmacaoNumero { get; set; }
    private int _confirmacoes;
    public Task<Registro> ObterAsync(ChaveRegistro chave, CancellationToken cancellationToken) => inner.ObterAsync(chave, cancellationToken);
    public Task<Registro?> ObterPrimeiraOutboxAsync(CancellationToken cancellationToken) => inner.ObterPrimeiraOutboxAsync(cancellationToken);
    public Task ConfirmarAsync(IReadOnlyCollection<AlteracaoRegistro> registros, CancellationToken cancellationToken)
        => ++_confirmacoes == FalharConfirmacaoNumero ? throw new IOException("Falha injetada antes da confirmação.")
            : inner.ConfirmarAsync(registros, cancellationToken);
}

internal sealed class PastaTemporaria : IDisposable
{
    public string Caminho { get; } = Path.Combine(Path.GetTempPath(), "execucao-test-" + Guid.NewGuid().ToString("N"));
    public PastaTemporaria() => Directory.CreateDirectory(Caminho);
    public void Dispose() => Directory.Delete(Caminho, true);
}
