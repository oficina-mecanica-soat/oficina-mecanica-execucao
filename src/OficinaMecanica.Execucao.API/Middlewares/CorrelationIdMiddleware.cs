using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.API.Middlewares;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemName = "CorrelationId";

    private const int MaxCorrelationIdLength = 128;
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private readonly IObservabilidadeExecucao _observabilidade;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger,
        IObservabilidadeExecucao observabilidade)
    {
        _next = next;
        _logger = logger;
        _observabilidade = observabilidade;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using var contextoTrace = _observabilidade.ContinuarContexto(new Dictionary<string, string>
        {
            ["traceparent"] = context.Request.Headers["traceparent"].FirstOrDefault() ?? ""
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["x_correlation_id"] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        var candidate = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();

        return IsValid(candidate) ? candidate! : Guid.NewGuid().ToString("N");
    }

    private static bool IsValid(string? candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate)
            && candidate.Length <= MaxCorrelationIdLength
            && candidate.All(character => !char.IsControl(character));
    }
}
