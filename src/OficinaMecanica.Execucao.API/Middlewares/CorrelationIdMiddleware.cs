using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.API.Middlewares;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger,
    IObservabilidadeExecucao observabilidade)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemName = "CorrelationId";
    public async Task InvokeAsync(HttpContext context)
    {
        var candidate = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        var correlation = !string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 128
            && candidate.All(c => !char.IsControl(c)) ? candidate : Guid.NewGuid().ToString("N");
        context.Items[ItemName] = correlation;
        context.Response.Headers[HeaderName] = correlation;
        using var contextoTrace = observabilidade.ContinuarContexto(new Dictionary<string, string>
        {
            ["traceparent"] = context.Request.Headers["traceparent"].FirstOrDefault() ?? ""
        });
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["x_correlation_id"] = correlation,
            ["operation"] = "http.request",
            ["bounded_context"] = "Execucao"
        });
        var inicio = Stopwatch.GetTimestamp();
        try { await next(context); }
        finally
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            logger.LogInformation("Requisição HTTP {@http} concluída em {duration_ms} ms.",
                new { method = context.Request.Method, route, status_code = context.Response.StatusCode },
                Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        }
    }
}
