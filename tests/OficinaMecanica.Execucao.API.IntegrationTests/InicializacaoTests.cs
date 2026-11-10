using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class InicializacaoTests
{
    [Fact]
    public async Task Dado_ServicoConfigurado_Quando_ConsultarHealth_Entao_RetornaHealthy()
    {
        await using var factory = CriarFactory(new string('a', 32));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chave-curta")]
    public async Task Dado_ChaveInvalida_Quando_Iniciar_Entao_RecusaConfiguracao(string? secret)
    {
        await using var factory = CriarFactory(secret);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Configure issuer, audience e chave JWT", exception.Message);
    }

    internal static WebApplicationFactory<Program> CriarFactory(string? secret)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = secret,
                    ["Jwt:Issuer"] = "oficina-mecanica-auth",
                    ["Jwt:Audience"] = "oficina-mecanica-api"
                })));
    }
}
