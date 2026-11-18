using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.API.Extensions.Configuration;
using OficinaMecanica.Execucao.API.Extensions.Middlewares;
using OficinaMecanica.Execucao.API.Middlewares;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class ConvencaoLogsHttpTests
{
    [Theory]
    [InlineData("/api/recurso/12345678901?senha=segredo-nao-registrar", 200, LogEventLevel.Information, true)]
    [InlineData("/rota-inexistente/12345678901?senha=segredo-nao-registrar", 404, LogEventLevel.Warning, true)]
    [InlineData("/api/health", 200, LogEventLevel.Debug, false)]
    [InlineData("/api/health", 503, LogEventLevel.Error, true)]
    public async Task Dado_Requisicao_Quando_Registrar_Entao_RespeitaConvencaoSemDadosSensiveis(
        string rota, int status, LogEventLevel nivel, bool registra)
    {
        // Arrange
        var sink = new Capturador();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
        builder.Services.AddSingleton<ILogEventSink>(sink);
        builder.Services.AddSingleton<OficinaMecanica.Execucao.Application.Mensagens.IObservabilidadeExecucao, OficinaMecanica.Execucao.Infrastructure.Observabilidade.ObservabilidadeDatadog>();
        builder.AddApiObservability();
        await using var app = builder.Build();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseApiRequestLogging();
        app.MapGet("/api/recurso/{id}", () => Results.StatusCode(status));
        app.MapGet("/api/health", () => Results.StatusCode(status));
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "jornada-segura");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer token-nao-registrar");

        // Act
        using var response = await client.GetAsync(rota);

        // Assert
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("jornada-segura", Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
        var eventos = sink.Eventos.Where(e => e.Properties.ContainsKey("http")).ToArray();
        if (!registra)
        {
            Assert.Empty(eventos);
            return;
        }
        var evento = Assert.Single(eventos);
        Assert.Equal(nivel, evento.Level);
        Assert.Equal("http.request", ((ScalarValue)evento.Properties["operation"]).Value);
        Assert.Equal("Execucao", ((ScalarValue)evento.Properties["bounded_context"]).Value);
        Assert.Equal("jornada-segura", ((ScalarValue)evento.Properties["x_correlation_id"]).Value);
        Assert.True(evento.Properties.ContainsKey("Elapsed"));
        Assert.True(evento.Properties.ContainsKey("service"));
        Assert.True(evento.Properties.ContainsKey("env"));
        Assert.True(evento.Properties.ContainsKey("version"));
        Assert.False(evento.Properties.ContainsKey("RequestPath"));
        Assert.StartsWith("Requisição HTTP", evento.MessageTemplate.Text);
        using var writer = new StringWriter();
        new JsonFormatter(renderMessage: true).Format(evento, writer);
        var json = writer.ToString();
        using var documento = JsonDocument.Parse(json);
        Assert.DoesNotContain("12345678901", json);
        Assert.DoesNotContain("token-nao-registrar", json);
        Assert.DoesNotContain("segredo-nao-registrar", json);
        Assert.Contains(status == 404 ? "unmatched" : rota.StartsWith("/api/health") ? "/api/health" : "/api/recurso/{id}", json);
    }

    [Fact]
    public async Task Dado_LogDeBiblioteca_Quando_UsarNivelNormal_Entao_PreservaOrigemEAvisosSemRuidoSql()
    {
        // Arrange
        var sink = new Capturador();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
        builder.Services.AddSingleton<ILogEventSink>(sink);
        builder.AddApiObservability();
        await using var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");

        // Act
        logger.LogInformation("Executed DbCommand: rotina que não deve poluir o painel.");
        logger.LogWarning("Library warning preserved.");

        // Assert
        var evento = Assert.Single(sink.Eventos);
        Assert.Equal(LogEventLevel.Warning, evento.Level);
        Assert.Equal("Library warning preserved.", evento.RenderMessage());
        Assert.Equal("Microsoft.EntityFrameworkCore.Database.Command", ((ScalarValue)evento.Properties["SourceContext"]).Value);
    }

    private sealed class Capturador : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Eventos { get; } = new();
        public void Emit(LogEvent logEvent) => Eventos.Enqueue(logEvent);
    }
}
