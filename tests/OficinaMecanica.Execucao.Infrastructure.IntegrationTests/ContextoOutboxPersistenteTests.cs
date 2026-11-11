using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class ContextoOutboxPersistenteTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Fact]
    public async Task Dado_TracePersistido_Quando_ReentregarERetomarOutbox_Entao_MantemContextoEIdentidadeSemNovaReserva()
    {
        using var c = await CenarioPreparacao.CriarAsync(fixture);
        await c.SemearAsync();
        const string trace = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var comando = c.Comando("PrepararExecucao", c.Dados()) with
        {
            TraceContext = new()
            { ["traceparent"] = trace, ["baggage"] = "nao-persistir", ["Authorization"] = "token-ficticio" }
        };
        var resultado = await c.EnviarAsync(comando);
        Assert.Equal(trace, Assert.Single(resultado.TraceContext!).Value);
        var reiniciado = CenarioPreparacao.CriarProcessador(c.Armazenamento, c.Relogio);
        var repetido = await c.EnviarAsync(comando with
        {
            MensagemId = Guid.NewGuid(),
            TraceContext = new()
            { ["traceparent"] = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-00" }
        }, reiniciado);
        Assert.Equal(resultado.MensagemId, repetido.MensagemId);
        Assert.Equal(trace, Assert.Single(repetido.TraceContext!).Value);
        Assert.Equal(7, (await c.SaldoAsync())!.Disponivel);
        var transporte = new TransporteControlado();
        var falha = new ArmazenamentoComFalha(c.Armazenamento) { FalharConfirmacaoNumero = 2 };
        await Assert.ThrowsAsync<IOException>(() => new PublicadorOutbox(falha, transporte, c.Relogio).PublicarProximaAsync());
        c.Relogio.Avancar(TimeSpan.FromMinutes(2));
        Assert.True(await new PublicadorOutbox(c.Armazenamento, transporte, c.Relogio).PublicarProximaAsync());
        Assert.Equal(2, transporte.Enviadas.Count);
        Assert.All(transporte.Enviadas, mensagem => Assert.Equal(trace, Assert.Single(mensagem.TraceContext!).Value));
        Assert.Equal(transporte.Enviadas[0].MensagemId, transporte.Enviadas[1].MensagemId);
    }
}
