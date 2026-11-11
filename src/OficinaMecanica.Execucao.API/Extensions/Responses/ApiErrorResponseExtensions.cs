using System.Text.Json;
using System.Text.Json.Serialization;
using OficinaMecanica.Execucao.API.Responses;
using OficinaMecanica.Execucao.Application.Common;

namespace OficinaMecanica.Execucao.API.Extensions.Responses;

public static class ApiErrorResponseExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task WriteApiErrorResponseAsJsonAsync(this HttpResponse response, ErrorResponse error)
    {
        response.ContentType = ApiResponseContentTypes.Json;

        return response.WriteAsJsonAsync(error, JsonOptions);
    }
}
