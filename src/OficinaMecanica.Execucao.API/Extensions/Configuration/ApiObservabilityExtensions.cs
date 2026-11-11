using System.Globalization;
using System.Reflection;
using Datadog.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace OficinaMecanica.Execucao.API.Extensions.Configuration;

public static class ApiObservabilityExtensions
{
    public static WebApplicationBuilder AddApiObservability(this WebApplicationBuilder builder)
    {
        var environment = Valor("DD_ENV", builder.Environment.EnvironmentName.ToLowerInvariant());
        var service = Valor("DD_SERVICE", "oficina-mecanica-execucao");
        var version = Valor("DD_VERSION", Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown");
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext().Enrich.With(new DadosHttpSegurosEnricher()).Enrich.With(new CorrelacaoDatadogEnricher())
            .Enrich.WithProperty("env", environment).Enrich.WithProperty("service", service).Enrich.WithProperty("version", version)
            .WriteTo.Console(new JsonFormatter(renderMessage: true)), preserveStaticLogger: true);
        return builder;
    }

    private static string Valor(string nome, string padrao) => Environment.GetEnvironmentVariable(nome) is { } valor
        && !string.IsNullOrWhiteSpace(valor) ? valor : padrao;

    private sealed class CorrelacaoDatadogEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            Adicionar("dd.trace_id", Convert.ToString(CorrelationIdentifier.TraceId, CultureInfo.InvariantCulture));
            Adicionar("dd.span_id", Convert.ToString(CorrelationIdentifier.SpanId, CultureInfo.InvariantCulture));
            void Adicionar(string nome, string? valor)
            {
                if (!string.IsNullOrEmpty(valor) && valor.Any(c => c != '0'))
                    logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(nome, valor));
            }
        }
    }

    private sealed class DadosHttpSegurosEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            // O scope padrão do ASP.NET adiciona RequestPath mesmo sem request logging.
            // O evento próprio já registra a rota do endpoint, sem valores da URL.
            foreach (var nome in new[] { "RequestPath", "RequestQueryString", "QueryString", "RequestHeaders", "RequestBody" })
                logEvent.RemovePropertyIfPresent(nome);
        }
    }
}
