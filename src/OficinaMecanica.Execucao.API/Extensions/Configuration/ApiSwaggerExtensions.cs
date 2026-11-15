using Microsoft.OpenApi;
using OficinaMecanica.Execucao.API.Filters;

namespace OficinaMecanica.Execucao.API.Extensions.Configuration;

public static class ApiSwaggerExtensions
{
    internal const string BearerScheme = "Bearer";

    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Oficina Mecânica — Execução",
                Version = "v1",
                Description = "Ações manuais do mecânico conforme a RFC-0007. Autorização de orçamento não inicia o reparo; respostas 202 são emitidas após commit local."
            });
            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Token JWT de Administrador ou Mecanico."
            });
            options.OperationFilter<AuthorizeOperationFilter>();
            options.OperationFilter<ApiSuccessResponsesOperationFilter>();
        });
        return services;
    }

    public static WebApplication UseApiSwagger(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")
            || (bool.TryParse(app.Configuration["Swagger:Enabled"], out var enabled) && enabled))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Oficina Mecanica Execucao v1");
                options.EnablePersistAuthorization();
            });
        }
        return app;
    }
}
