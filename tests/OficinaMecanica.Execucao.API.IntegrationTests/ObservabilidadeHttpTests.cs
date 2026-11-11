using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Execucao.API.Middlewares;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class ObservabilidadeHttpTests
{
    [Fact]
    public async Task Dado_RequisicaoComDadosSensiveis_Quando_RegistrarJson_Entao_UsaRotaTemplatizadaECorrelacaoSemPayload()
    {
        var sink = new Capturador();
        await using var factory = ExecucoesHttpFactory.Criar(configurar: services => services.AddSingleton<ILogEventSink>(sink));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer token-nao-registrar");
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "jornada-f4-11");
        using var response = await client.GetAsync("/rota-inexistente/12345678901?senha=segredo-nao-registrar");
        Assert.Equal("jornada-f4-11", Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
        var log = Assert.Single(sink.Eventos, evento => evento.Properties.ContainsKey("http"));
        Assert.True(log.Properties.ContainsKey("service"));
        Assert.True(log.Properties.ContainsKey("env"));
        Assert.True(log.Properties.ContainsKey("version"));
        Assert.Equal("jornada-f4-11", ((ScalarValue)log.Properties["x_correlation_id"]).Value);
        using var writer = new StringWriter();
        new JsonFormatter(renderMessage: true).Format(log, writer);
        var json = writer.ToString();
        _ = JsonDocument.Parse(json);
        Assert.DoesNotContain("12345678901", json);
        Assert.DoesNotContain("token-nao-registrar", json);
        Assert.DoesNotContain("segredo-nao-registrar", json);
        Assert.Contains("unmatched", json);
        Assert.False(log.Properties.ContainsKey("dd.trace_id")); // Sem profiler, não fabricar IDs de Datadog.
    }

    [Fact]
    public async Task Dado_CorrelacaoExcessivaOuAusente_Quando_Responder_Entao_GeraIdentificadorSeguro()
    {
        await using var factory = ExecucoesHttpFactory.Criar();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, new string('a', 129));
        using var response = await client.GetAsync("/api/health");
        Assert.True(Guid.TryParse(Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)), out _));
    }

    private sealed class Capturador : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Eventos { get; } = new();
        public void Emit(LogEvent logEvent) => Eventos.Enqueue(logEvent);
    }
}
