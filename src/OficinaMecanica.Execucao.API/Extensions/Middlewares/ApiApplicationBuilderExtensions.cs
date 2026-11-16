using OficinaMecanica.Execucao.API.Middlewares;
using Microsoft.AspNetCore.Routing;
using Serilog;
using Serilog.Events;

namespace OficinaMecanica.Execucao.API.Extensions.Middlewares;

public static class ApiApplicationBuilderExtensions
{
    public static WebApplication UseApiRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            options.MessageTemplate = "Requisição HTTP {@http} concluída em {Elapsed:0.0000} ms.";
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : context.Response.StatusCode >= 400
                        ? LogEventLevel.Warning
                        : context.Request.Path.Equals("/api/health")
                            ? LogEventLevel.Debug
                            : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnosticContext, context) =>
            {
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";

                diagnosticContext.Set("operation", "http.request");
                diagnosticContext.Set(
                    "http",
                    new
                    {
                        method = context.Request.Method,
                        route,
                        status_code = context.Response.StatusCode
                    },
                    destructureObjects: true);

                if (context.Items.TryGetValue(CorrelationIdMiddleware.ItemName, out var correlationId))
                {
                    diagnosticContext.Set("x_correlation_id", correlationId);
                }
            };
        });

        return app;
    }
}
