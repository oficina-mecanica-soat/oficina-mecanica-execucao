using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OficinaMecanica.Execucao.API.Filters;

internal sealed class ApiSuccessResponsesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses?.TryGetValue(StatusCodes.Status202Accepted.ToString(), out var aceita) != true
            || aceita is not OpenApiResponse resposta)
        {
            return;
        }
        resposta.Headers ??= new Dictionary<string, IOpenApiHeader>();
        resposta.Headers["Location"] = new OpenApiHeader
        {
            Description = "URL para consultar o estado local da preparação/execução após o commit.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" }
        };
    }
}
