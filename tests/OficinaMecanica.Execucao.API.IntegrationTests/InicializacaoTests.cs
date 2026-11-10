using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
        var api = typeof(Program).Assembly.Location;
        var inicio = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(api)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        inicio.ArgumentList.Add(api);
        inicio.Environment["Jwt__Secret"] = secret ?? string.Empty;
        inicio.Environment["Jwt__Issuer"] = "oficina-mecanica-auth";
        inicio.Environment["Jwt__Audience"] = "oficina-mecanica-api";
        inicio.Environment["ProcessamentoLocal__Habilitado"] = "false";
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        inicio.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        using var processo = Process.Start(inicio)!;
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erro = processo.StandardError.ReadToEndAsync();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await processo.WaitForExitAsync(limite.Token);
            Assert.NotEqual(0, processo.ExitCode);
            Assert.Contains("Configure issuer, audience e chave JWT", await saida + await erro);
        }
        finally
        {
            if (!processo.HasExited)
            {
                processo.Kill(entireProcessTree: true);
                await processo.WaitForExitAsync();
            }
        }
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
