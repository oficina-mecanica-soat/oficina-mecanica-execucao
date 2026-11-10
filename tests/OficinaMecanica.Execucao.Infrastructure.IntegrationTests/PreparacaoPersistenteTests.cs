using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class PreparacaoPersistenteTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Fact]
    public async Task Dado_MesmoSnapshotComMetadadosOuEscalaDecimalDiferentes_Quando_Reentregar_Entao_Deduplica()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var dados = cenario.Dados();
        var comando = cenario.Comando("PrepararExecucao", dados);
        var primeira = await cenario.EnviarAsync(comando);
        var outraEscala = dados with { Servicos = [dados.Servicos[0] with { Valor = 125.5m }] };
        var repetido = comando with
        {
            MensagemId = Guid.NewGuid(),
            SagaId = Guid.NewGuid(),
            CorrelationId = "outra-correlacao",
            Dados = JsonSerializer.SerializeToElement(outraEscala, JsonContrato.Options)
        };

        var segunda = await cenario.EnviarAsync(repetido);

        Assert.Equal(primeira.MensagemId, segunda.MensagemId);
        Assert.Equal(primeira.SagaId, segunda.SagaId);
        Assert.Equal(7, (await cenario.SaldoAsync())!.Disponivel);
    }

    [Fact]
    public async Task Dado_ReservaPersistidaAusente_Quando_Cancelar_Entao_NaoConfirmaLiberacao()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var reserva = await cenario.Armazenamento.ObterAsync(ChavesExecucao.Reserva(cenario.OsId, cenario.ItemId, 1), default);
        await cenario.Armazenamento.ConfirmarAsync([new(reserva, true, null)], default);

        await Assert.ThrowsAsync<DomainException>(() => cenario.CancelarAsync(preparada));

        Assert.Equal(7, (await cenario.SaldoAsync())!.Disponivel);
        Assert.Equal(3, (await cenario.SaldoAsync())!.Reservada);
    }

    [Fact]
    public async Task Dado_MesmaOperacaoConcorrente_Quando_PrepararECancelar_Entao_DeduplicaAmbas()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados());
        var resultados = await Task.WhenAll(cenario.EnviarAsync(comando), cenario.EnviarAsync(comando with { MensagemId = Guid.NewGuid() }));
        Assert.Equal(resultados[0].MensagemId, resultados[1].MensagemId);
        Assert.Equal(3, (await cenario.SaldoAsync())!.Reservada);
        var id = resultados[0].Dados.GetProperty("preparacaoId").GetGuid();
        var cancelar = cenario.Comando("CancelarPreparacao", new CancelarPreparacao(id, "Recusa"));

        var canceladas = await Task.WhenAll(cenario.EnviarAsync(cancelar), cenario.EnviarAsync(cancelar with { MensagemId = Guid.NewGuid() }));

        Assert.Equal(canceladas[0].MensagemId, canceladas[1].MensagemId);
        Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
        var reserva = (await cenario.Armazenamento.ObterAsync(ChavesExecucao.Reserva(cenario.OsId, cenario.ItemId, 1), default)).Ler<ReservaPersistida>();
        Assert.True(reserva!.Liberada);
        Assert.Equal(3, reserva.Quantidade);
    }

    [Fact]
    public async Task Dado_VersoesSucessivas_Quando_ReentregarCancelamentoAntigo_Entao_PreservaReservaAtualEHistorico()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var id = preparada.Dados.GetProperty("preparacaoId").GetGuid();
        var cancelar = cenario.Comando("CancelarPreparacao", new CancelarPreparacao(id, "Recusa"));
        var primeira = await cenario.EnviarAsync(cancelar);
        await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados(2, 2)));

        var repetida = await cenario.EnviarAsync(cancelar with { MensagemId = Guid.NewGuid() });

        Assert.Equal(primeira.MensagemId, repetida.MensagemId);
        Assert.Equal(8, (await cenario.SaldoAsync())!.Disponivel);
        Assert.True((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Reserva(cenario.OsId, cenario.ItemId, 1), default)).Ler<ReservaPersistida>()!.Liberada);
        Assert.False((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Reserva(cenario.OsId, cenario.ItemId, 2), default)).Ler<ReservaPersistida>()!.Liberada);
    }

    [Fact]
    public async Task Dado_LoteNoLimiteAtomico_Quando_Preparar_Entao_GravaTodasAsReservas()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var itens = Enumerable.Range(0, 46).Select(_ => new ItemSolicitado(Guid.NewGuid(), 1)).ToArray();
        foreach (var item in itens) await cenario.SemearAsync(1, item.ItemId);

        var resultado = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = itens }));

        Assert.Equal("ExecucaoPreparada", resultado.Tipo);
        foreach (var item in itens)
        {
            Assert.Equal(0, (await cenario.SaldoAsync(item.ItemId))!.Disponivel);
            Assert.NotNull((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Reserva(cenario.OsId, item.ItemId, 1), default)).Ler<ReservaPersistida>());
        }
    }

    [Fact]
    public async Task Dado_PreparacaoValida_Quando_ReiniciarEReentregarComOutroId_Entao_ReservaUmaVez()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados());
        var primeira = await cenario.EnviarAsync(comando);
        using var novoClient = fixture.CriarClient();
        var novo = CenarioPreparacao.CriarProcessador(new ArmazenamentoDynamoDb(novoClient, cenario.Tabela), cenario.Relogio);

        var segunda = await cenario.EnviarAsync(comando with { MensagemId = Guid.NewGuid() }, novo);

        Assert.Equal(JsonContrato.Serializar(primeira), JsonContrato.Serializar(segunda));
        Assert.Equal("ExecucaoPreparada", primeira.Tipo);
        Assert.Equal(comando.MensagemId, primeira.CausationId);
        Assert.Equal(comando.CorrelationId, primeira.CorrelationId);
        Assert.Equal(comando.SagaId, primeira.SagaId);
        Assert.Equal(7, (await cenario.SaldoAsync())!.Disponivel);
        Assert.Equal(3, (await cenario.SaldoAsync())!.Reservada);
        Assert.NotNull(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
    }

    [Fact]
    public async Task Dado_DuasOsConcorrentes_Quando_DisputarUltimoSaldo_Entao_UmaReservaEOuTraRecusa()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync(3);
        var dados = cenario.Dados();
        var resultados = await Task.WhenAll(cenario.EnviarAsync(cenario.Comando("PrepararExecucao", dados)),
            cenario.EnviarAsync(cenario.Comando("PrepararExecucao", dados, Guid.NewGuid())));

        Assert.Single(resultados, resultado => resultado.Tipo == "ExecucaoPreparada");
        Assert.Single(resultados, resultado => resultado.Tipo == "PreparacaoRecusada");
        Assert.Equal(0, (await cenario.SaldoAsync())!.Disponivel);
        Assert.Equal(3, (await cenario.SaldoAsync())!.Reservada);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dado_SegundoItemSemSaldoOuAusente_Quando_Preparar_Entao_PersisteRecusaSemReservaParcial(bool existente)
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        if (existente) await cenario.SemearAsync(1, cenario.SegundoItemId);
        var dados = cenario.Dados() with { Itens = [new(cenario.ItemId, 3), new(cenario.SegundoItemId, 2)] };
        var comando = cenario.Comando("PrepararExecucao", dados);

        var resultado = await cenario.EnviarAsync(comando);
        var repetido = await cenario.EnviarAsync(comando);

        Assert.Equal("PreparacaoRecusada", resultado.Tipo);
        Assert.Equal("ESTOQUE_INSUFICIENTE", resultado.Dados.GetProperty("codigoMotivo").GetString());
        Assert.Equal(1, resultado.Dados.GetProperty("versao").GetInt32());
        Assert.Equal(resultado.MensagemId, repetido.MensagemId);
        Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
        Assert.Equal(0, (await cenario.SaldoAsync())!.Reservada);
    }

    [Fact]
    public async Task Dado_FiliaisDistintas_Quando_Preparar_Entao_NaoConsomeSaldoAlheio()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var outra = Guid.NewGuid();
        await cenario.SemearAsync(10, filialId: outra);

        var resultado = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));

        Assert.Equal("PreparacaoRecusada", resultado.Tipo);
        Assert.Equal(10, (await cenario.SaldoAsync(filialId: outra))!.Disponivel);
    }

    [Fact]
    public async Task Dado_CancelamentoDuplicadoAposReinicio_Quando_Reprocessar_Entao_DevolveSaldoUmaVez()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var preparar = cenario.Comando("PrepararExecucao", cenario.Dados());
        var preparada = await cenario.EnviarAsync(preparar);
        var id = preparada.Dados.GetProperty("preparacaoId").GetGuid();
        var cancelar = cenario.Comando("CancelarPreparacao", new CancelarPreparacao(id, "Recusado"));
        var primeira = await cenario.EnviarAsync(cancelar);
        using var client = fixture.CriarClient();
        var processador = CenarioPreparacao.CriarProcessador(new ArmazenamentoDynamoDb(client, cenario.Tabela), cenario.Relogio);

        var segunda = await cenario.EnviarAsync(cancelar with { MensagemId = Guid.NewGuid() }, processador);
        var tardia = await cenario.EnviarAsync(preparar with { MensagemId = Guid.NewGuid() }, processador);

        Assert.Equal("PreparacaoCancelada", primeira.Tipo);
        Assert.Equal(primeira.MensagemId, segunda.MensagemId);
        Assert.Equal(preparada.MensagemId, tardia.MensagemId);
        Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
        Assert.Equal(0, (await cenario.SaldoAsync())!.Reservada);
        Assert.False((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Atual(cenario.OsId), default)).Ler<PreparacaoAtual>()!.Ativa);
    }

    [Fact]
    public async Task Dado_PreparacaoSemPecas_Quando_Cancelar_Entao_ConfirmaSemReserva()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));

        var cancelada = await cenario.CancelarAsync(preparada);

        Assert.Equal("PreparacaoCancelada", cancelada.Tipo);
        Assert.Null(await cenario.SaldoAsync());
    }

    [Fact]
    public async Task Dado_NovaVersao_Quando_AnteriorAtiva_Entao_RecusaEAposCancelarAceita()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var nova = cenario.Comando("PrepararExecucao", cenario.Dados(2, 2));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(nova));
        await cenario.CancelarAsync(preparada);

        var resultado = await cenario.EnviarAsync(nova);

        Assert.Equal(2, resultado.Dados.GetProperty("versao").GetInt32());
        Assert.Equal(1, resultado.Versao);
        Assert.Equal(8, (await cenario.SaldoAsync())!.Disponivel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dado_SnapshotDivergente_Quando_ReusarOperacaoOuMensagem_Entao_NaoAlteraSaldo(bool mesmoMensagemId)
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var dados = cenario.Dados();
        var comando = cenario.Comando("PrepararExecucao", dados);
        await cenario.EnviarAsync(comando);
        var divergente = cenario.Comando("PrepararExecucao", dados with { Itens = [new(cenario.ItemId, 2)] });
        if (mesmoMensagemId) divergente = divergente with { MensagemId = comando.MensagemId };

        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(divergente));

        Assert.Equal(7, (await cenario.SaldoAsync())!.Disponivel);
    }

    [Fact]
    public async Task Dado_CancelamentoDesconhecidoOuDeOutraOs_Quando_Processar_Entao_NaoConfirmaCompensacao()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await Assert.ThrowsAsync<PreparacaoNaoEncontradaException>(() => cenario.EnviarAsync(cenario.Comando(
            "CancelarPreparacao", new CancelarPreparacao(Guid.NewGuid(), "Recusa"))));
        await cenario.SemearAsync();
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var id = preparada.Dados.GetProperty("preparacaoId").GetGuid();

        await Assert.ThrowsAsync<ContratoMensagemException>(() => cenario.EnviarAsync(cenario.Comando(
            "CancelarPreparacao", new CancelarPreparacao(id, "Recusa"), Guid.NewGuid())));

        Assert.Equal(3, (await cenario.SaldoAsync())!.Reservada);
    }

    [Fact]
    public async Task Dado_FalhaNaCompensacao_Quando_RestaurarSaldoEReentregar_Entao_RetomaSemSucessoFicticio()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var preparada = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var key = ChavesExecucao.Estoque(cenario.FilialId, cenario.ItemId);
        var registro = await cenario.Armazenamento.ObterAsync(key, default);
        var saldo = registro.Ler<SaldoEstoque>()!;
        await cenario.Armazenamento.ConfirmarAsync([new(registro, true, JsonContrato.Serializar(saldo with { Reservada = 0 }))], default);
        await Assert.ThrowsAsync<DomainException>(() => cenario.CancelarAsync(preparada));
        Assert.True((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Atual(cenario.OsId), default)).Ler<PreparacaoAtual>()!.Ativa);
        registro = await cenario.Armazenamento.ObterAsync(key, default);
        await cenario.Armazenamento.ConfirmarAsync([new(registro, true, JsonContrato.Serializar(saldo))], default);

        await cenario.CancelarAsync(preparada);

        Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
    }

    [Fact]
    public async Task Dado_CondicaoDeConcorrenciaInvalida_Quando_ConfirmarLote_Entao_NaoGravaNenhumRegistro()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var key = ChavesExecucao.Estoque(cenario.FilialId, cenario.ItemId);
        var original = await cenario.Armazenamento.ObterAsync(key, default);
        await cenario.Armazenamento.ConfirmarAsync([new(original, true, original.Json)], default);
        var fantasma = new Registro(new("TESTE", "NAO_GRAVAR"), 0, null);

        await Assert.ThrowsAsync<ConflitoConcorrenciaException>(() => cenario.Armazenamento.ConfirmarAsync([
            new(original, true, original.Json), new(fantasma, true, "{}")], default));

        Assert.Null((await cenario.Armazenamento.ObterAsync(fantasma.Chave, default)).Json);
    }
}
