using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class CicloExecucaoPersistenteTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Fact]
    public async Task Dado_OrcamentoAutorizado_Quando_IniciarEFinalizarManualmente_Entao_PersisteEventosEBaixaUmaVez()
    {
        using var c = await Cenario.CriarAsync(fixture, doisServicos: true);
        await c.AutorizarAsync();
        var estado = await c.EstadoAsync();
        Assert.All(estado.Execucao!.Servicos, s => Assert.Equal(StatusServico.Pendente, s.Status));
        var servicoPersistido = Assert.Single(estado.Execucao.Servicos, s => s.Id == c.Dados.Servicos[0].ServicoId);
        Assert.Equal(c.Dados.Servicos[0].ServicoCatalogoId, servicoPersistido.ServicoCatalogoId);
        Assert.Equal(7, estado.Autorizacao!.VersaoOrcamento); // Preparação 1 não é orçamento 7.
        Assert.Empty(await c.ResultadosAsync());

        var primeiro = await c.IniciarAsync(0);
        c.Base.Relogio.Avancar(TimeSpan.FromMinutes(1));
        var repetido = await c.IniciarAsync(0);
        Assert.Equal(primeiro.Execucao.Servicos.Single(s => s.Id == c.Dados.Servicos[0].ServicoId).DataInicio,
            repetido.Execucao.Servicos.Single(s => s.Id == c.Dados.Servicos[0].ServicoId).DataInicio);
        await c.IniciarAsync(1);
        var eventos = await c.ResultadosAsync();
        Assert.Equal("ExecucaoIniciada", Assert.Single(eventos).Tipo);
        Assert.Equal(c.Autorizacao.SagaId, eventos[0].SagaId);
        Assert.Equal(c.Autorizacao.CorrelationId, eventos[0].CorrelationId);

        await c.FinalizarServicoAsync(0);
        await Assert.ThrowsAsync<DomainException>(() => c.FinalizarAsync());
        Assert.Equal(3, (await c.Base.SaldoAsync())!.Reservada);
        await c.FinalizarServicoAsync(1);
        Assert.Empty(await c.ResultadosAsync());
        Assert.Null((await c.EstadoAsync()).Execucao!.FinalizadaEm);

        var final = await c.FinalizarAsync();
        // Novas instâncias leem somente DynamoDB e não repetem baixa nem evento.
        var reiniciado = CenarioPreparacao.CriarProcessador(c.Base.Armazenamento, c.Base.Relogio);
        Assert.Null(await reiniciado.ProcessarAsync(JsonContrato.Serializar(c.Autorizacao with { MensagemId = Guid.NewGuid() })));
        var reentrega = await c.FinalizarAsync();
        Assert.Equal(final.Execucao.FinalizadaEm, reentrega.Execucao.FinalizadaEm);
        Assert.Equal(7, (await c.Base.SaldoAsync())!.Disponivel);
        Assert.Equal(0, (await c.Base.SaldoAsync())!.Reservada);
        var eventoFinal = Assert.Single(await c.ResultadosAsync());
        Assert.Equal("ExecucaoFinalizada", eventoFinal.Tipo);
        Assert.Equal(final.Execucao.FinalizadaEm, eventoFinal.Dados.Deserialize<ExecucaoFinalizada>(JsonContrato.Options)!.FinalizadoEm.UtcDateTime);
        Assert.Empty(await c.ResultadosAsync());
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.Base.CancelarAsync(c.Preparada));
    }

    [Fact]
    public async Task Dado_AutorizacaoReentregue_Quando_ConfirmarECancelarAntesDoInicio_Entao_NaoPublicaInicioNemReativaReserva()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        Assert.Null(await c.Base.Processador.ProcessarAsync(JsonContrato.Serializar(c.Autorizacao with { MensagemId = Guid.NewGuid() })));
        var cancelada = await c.Base.CancelarAsync(c.Preparada);
        Assert.Equal("PreparacaoCancelada", cancelada.Tipo);
        Assert.Null(await c.Base.Processador.ProcessarAsync(JsonContrato.Serializar(c.Autorizacao)));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.IniciarAsync(0));
        Assert.Equal(10, (await c.Base.SaldoAsync())!.Disponivel);
        Assert.Equal(0, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Equal("PreparacaoCancelada", Assert.Single(await c.ResultadosAsync()).Tipo);
    }

    [Fact]
    public async Task Dado_AutorizacaoDivergente_Quando_ReceberOutraVersaoOuSaga_Entao_NaoSobrescreveAOriginal()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.Base.Processador.ProcessarAsync(JsonContrato.Serializar(
            c.Autorizacao with { MensagemId = Guid.NewGuid(), Dados = JsonSerializer.SerializeToElement(new AutorizarExecucao(c.PreparacaoId, 8), JsonContrato.Options) })));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.Base.Processador.ProcessarAsync(JsonContrato.Serializar(
            c.Autorizacao with { MensagemId = Guid.NewGuid(), SagaId = Guid.NewGuid() })));
        Assert.Equal(7, (await c.EstadoAsync()).Autorizacao!.VersaoOrcamento);
        Assert.Empty(await c.ResultadosAsync());
    }

    [Fact]
    public async Task Dado_ExecucaoSemAutorizacao_Quando_AcionarMecanico_Entao_NaoAlteraServicosNemEstoque()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.IniciarAsync(0));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.FinalizarServicoAsync(0));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.FinalizarAsync());
        Assert.Equal(3, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Empty(await c.ResultadosAsync());
    }

    [Fact]
    public async Task Dado_PreparacaoCanceladaOuDeOutraOs_Quando_Autorizar_Entao_NaoLiberaExecucao()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.Base.Processador.ProcessarAsync(JsonContrato.Serializar(c.Autorizacao with { OrdemServicoId = Guid.NewGuid() })));
        await c.Base.CancelarAsync(c.Preparada);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.AutorizarAsync());
        Assert.Null((await c.EstadoAsync()).Autorizacao);
    }

    [Fact]
    public async Task Dado_IniciosConcorrentes_Quando_DisputarPrimeiroServico_Entao_PublicaApenasUmInicio()
    {
        using var c = await Cenario.CriarAsync(fixture, doisServicos: true);
        await c.AutorizarAsync();
        var barreira = new BarreiraCommit(c.Base.Armazenamento);
        await Task.WhenAll(c.IniciarAsync(0, armazenamento: barreira), c.IniciarAsync(1, armazenamento: barreira));
        Assert.All((await c.EstadoAsync()).Execucao!.Servicos, s => Assert.Equal(StatusServico.EmExecucao, s.Status));
        Assert.Equal("ExecucaoIniciada", Assert.Single(await c.ResultadosAsync()).Tipo);
    }

    [Fact]
    public async Task Dado_CancelamentoEInicioConcorrentes_Quando_DisputarPreparacao_Entao_SomenteUmConfirma()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        var barreira = new BarreiraCommit(c.Base.Armazenamento);
        var inicio = Record.ExceptionAsync(() => c.IniciarAsync(0, armazenamento: barreira));
        var cancelamento = Record.ExceptionAsync(() => c.Base.EnviarAsync(c.Base.Comando("CancelarPreparacao",
            new CancelarPreparacao(c.PreparacaoId, "Recusa")), CenarioPreparacao.CriarProcessador(barreira, c.Base.Relogio)));
        var resultados = await Task.WhenAll(inicio, cancelamento);
        Assert.Single(resultados, erro => erro is null);
        Assert.IsType<ContratoMensagemException>(Assert.Single(resultados, erro => erro is not null));
        var estado = await c.EstadoAsync();
        Assert.NotEqual(estado.Cancelada, estado.Execucao!.Reconstituir().IniciadaEm is not null);
        var saldo = (await c.Base.SaldoAsync())!;
        Assert.Equal(estado.Cancelada ? 10 : 7, saldo.Disponivel);
        Assert.Single(await c.ResultadosAsync());
    }

    [Fact]
    public async Task Dado_FinalizacoesConcorrentes_Quando_BaixarEstoque_Entao_ConfirmaUmaUnicaBaixaEEvento()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        await c.IniciarAsync(0);
        await c.FinalizarServicoAsync(0);
        await c.ResultadosAsync();
        var barreira = new BarreiraCommit(c.Base.Armazenamento);
        await Task.WhenAll(c.FinalizarAsync(barreira), c.FinalizarAsync(barreira));
        Assert.Equal(0, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Equal("ExecucaoFinalizada", Assert.Single(await c.ResultadosAsync()).Tipo);
    }

    [Fact]
    public async Task Dado_FalhaNoCommit_Quando_FinalizarERetomar_Entao_BaixaEstadoEOutboxPermanecemAtomicos()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        await c.IniciarAsync(0);
        await c.FinalizarServicoAsync(0);
        await c.ResultadosAsync();
        var falha = new ArmazenamentoComFalha(c.Base.Armazenamento) { FalharConfirmacaoNumero = 1 };
        await Assert.ThrowsAsync<IOException>(() => c.FinalizarAsync(falha));
        Assert.Null((await c.EstadoAsync()).Execucao!.FinalizadaEm);
        Assert.Equal(3, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Empty(await c.ResultadosAsync());
        await c.FinalizarAsync();
        Assert.Equal(0, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Single(await c.ResultadosAsync());
    }

    [Fact]
    public async Task Dado_OperacaoIdReutilizado_Quando_MudarAcao_Entao_RejeitaSemFinalizarServico()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        var id = Guid.NewGuid();
        await c.IniciarAsync(0, id);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => c.FinalizarServicoAsync(0, id));
        Assert.Equal(StatusServico.EmExecucao, (await c.EstadoAsync()).Execucao!.Servicos[0].Status);
    }

    [Fact]
    public async Task Dado_ReservaInconsistente_Quando_Finalizar_Entao_NaoConfirmaBaixaEstadoOuEvento()
    {
        using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        await c.IniciarAsync(0);
        await c.FinalizarServicoAsync(0);
        await c.ResultadosAsync();
        var tx = new TransacaoExecucao(c.Base.Armazenamento);
        var chave = ChavesExecucao.Reserva(c.Base.OsId, c.Base.ItemId, 1);
        var reserva = (await tx.LerAsync<ReservaPersistida>(chave, default))!;
        tx.Salvar(chave, reserva with { Baixada = true });
        await tx.ConfirmarAsync(default);

        await Assert.ThrowsAsync<DomainException>(() => c.FinalizarAsync());

        Assert.Equal(3, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Null((await c.EstadoAsync()).Execucao!.FinalizadaEm);
        Assert.Empty(await c.ResultadosAsync());
    }

    [Fact]
    public async Task Dado_SnapshotLegadoSemCatalogo_Quando_Autorizar_Entao_ExigeReconciliacaoSemFabricarId()
    {
        using var c = await Cenario.CriarAsync(fixture);
        var tx = new TransacaoExecucao(c.Base.Armazenamento);
        var chave = ChavesExecucao.Preparacao(c.Base.OsId, 1);
        var estado = (await tx.LerAsync<EstadoPreparacao>(chave, default))!;
        tx.Salvar(chave, estado with
        {
            Snapshot = estado.Snapshot with
            { Servicos = [estado.Snapshot.Servicos[0] with { ServicoCatalogoId = Guid.Empty }] }
        });
        await tx.ConfirmarAsync(default);

        await Assert.ThrowsAsync<DomainException>(() => c.AutorizarAsync());

        Assert.Null((await c.EstadoAsync()).Autorizacao);
        Assert.Equal(3, (await c.Base.SaldoAsync())!.Reservada);
        Assert.Empty(await c.ResultadosAsync());
    }

    private sealed class Cenario(CenarioPreparacao cenario, PrepararExecucao dados, Mensagem preparada) : IDisposable
    {
        public CenarioPreparacao Base => cenario;
        public PrepararExecucao Dados => dados;
        public Mensagem Preparada => preparada;
        public Guid PreparacaoId => preparada.Dados.Deserialize<ExecucaoPreparada>(JsonContrato.Options)!.PreparacaoId;
        public Mensagem Autorizacao { get; } = cenario.Comando("AutorizarExecucao", new AutorizarExecucao(
            preparada.Dados.Deserialize<ExecucaoPreparada>(JsonContrato.Options)!.PreparacaoId, 7));
        public static async Task<Cenario> CriarAsync(DynamoDbFixture fixture, bool doisServicos = false)
        {
            var c = await CenarioPreparacao.CriarAsync(fixture);
            await c.SemearAsync();
            var dados = c.Dados();
            if (doisServicos) dados = dados with { Servicos = [.. dados.Servicos, new(Guid.NewGuid(), "Revisão", 10m, Guid.NewGuid())] };
            var resultado = await c.EnviarAsync(c.Comando("PrepararExecucao", dados));
            var cenario = new Cenario(c, dados, resultado);
            await cenario.ResultadosAsync();
            return cenario;
        }
        public Task<Mensagem?> AutorizarAsync() => Base.Processador.ProcessarAsync(JsonContrato.Serializar(Autorizacao));
        public Task<ExecucaoResponse> IniciarAsync(int indice, Guid? id = null, IArmazenamentoExecucao? armazenamento = null) => new IniciarExecucaoServicoUseCase(
            new UnidadeTrabalhoPreparacaoDynamoDb(armazenamento ?? Base.Armazenamento), new IniciarExecucaoServicoValidator(), Base.Relogio)
            .ExecuteAsync(new(id ?? Guid.NewGuid(), Base.OsId, PreparacaoId, dados.Servicos[indice].ServicoId));
        public Task<ExecucaoResponse> FinalizarServicoAsync(int indice, Guid? id = null) => new FinalizarServicoUseCase(
            new UnidadeTrabalhoPreparacaoDynamoDb(Base.Armazenamento), new FinalizarServicoValidator(), Base.Relogio)
            .ExecuteAsync(new(id ?? Guid.NewGuid(), Base.OsId, PreparacaoId, dados.Servicos[indice].ServicoId));
        public Task<ExecucaoResponse> FinalizarAsync(IArmazenamentoExecucao? armazenamento = null) => new FinalizarExecucaoUseCase(
            new UnidadeTrabalhoPreparacaoDynamoDb(armazenamento ?? Base.Armazenamento), new FinalizarExecucaoValidator(), Base.Relogio)
            .ExecuteAsync(new(Guid.NewGuid(), Base.OsId, PreparacaoId));
        public async Task<EstadoPreparacao> EstadoAsync() => (await Base.Armazenamento.ObterAsync(
            ChavesExecucao.Preparacao(Base.OsId, 1), default)).Ler<EstadoPreparacao>()!;
        public async Task<IReadOnlyList<Mensagem>> ResultadosAsync()
        {
            var transporte = new Capturador();
            var publicador = new PublicadorOutbox(Base.Armazenamento, transporte, Base.Relogio);
            while (await publicador.PublicarProximaAsync()) { }
            return transporte.Mensagens;
        }
        public void Dispose() => Base.Dispose();
    }
    private sealed class Capturador : ITransporteResultado
    {
        public List<Mensagem> Mensagens { get; } = [];
        public Task PublicarAsync(Mensagem mensagem, CancellationToken ct) { Mensagens.Add(mensagem); return Task.CompletedTask; }
    }

    private sealed class BarreiraCommit(IArmazenamentoExecucao inner) : IArmazenamentoExecucao
    {
        private int _confirmacoes;
        private readonly TaskCompletionSource _duasLeituras = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Registro> ObterAsync(ChaveRegistro chave, CancellationToken ct) => inner.ObterAsync(chave, ct);
        public Task<Registro?> ObterPrimeiraOutboxAsync(CancellationToken ct) => inner.ObterPrimeiraOutboxAsync(ct);
        public async Task ConfirmarAsync(IReadOnlyCollection<AlteracaoRegistro> registros, CancellationToken ct)
        {
            var numero = Interlocked.Increment(ref _confirmacoes);
            if (numero == 2) _duasLeituras.TrySetResult();
            if (numero <= 2) await _duasLeituras.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await inner.ConfirmarAsync(registros, ct);
        }
    }
}
