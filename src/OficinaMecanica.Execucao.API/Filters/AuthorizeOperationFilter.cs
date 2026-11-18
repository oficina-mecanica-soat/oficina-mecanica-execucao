using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using OficinaMecanica.Execucao.API.Extensions.Configuration;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OficinaMecanica.Execucao.API.Filters;

internal sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiSwaggerExtensions.BearerScheme, context.Document, null)] = []
        });
    }
}
