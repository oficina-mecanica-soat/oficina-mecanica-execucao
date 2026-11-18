using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class InicializacaoTests
{
    [Theory]
    [InlineData("Development", false, HttpStatusCode.OK)]
    [InlineData("Testing", false, HttpStatusCode.OK)]
    [InlineData("Staging", false, HttpStatusCode.NotFound)]
    [InlineData("Production", false, HttpStatusCode.NotFound)]
    [InlineData("Staging", true, HttpStatusCode.OK)]
    public async Task Dado_AmbienteConfigurado_Quando_ConsultarSwagger_Entao_RespeitaHabilitacaoDaReferencia(string ambiente, bool habilitado, HttpStatusCode esperado)
    {
        await using var factory = ExecucoesHttpFactory.Criar(ambiente).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Swagger:Enabled"] = habilitado.ToString() })));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(esperado, response.StatusCode);
    }

    [Fact]
    public async Task Dado_ContratoSwagger_Quando_Consultar_Entao_DocumentaJwtAceitacaoEOperacaoObrigatoria()
    {
        await using var factory = ExecucoesHttpFactory.Criar();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var documento = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var raiz = documento.RootElement;
        var esquema = raiz.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", esquema.GetProperty("type").GetString());
        Assert.Equal("bearer", esquema.GetProperty("scheme").GetString());
        const string baseRota = "/api/v1/execucoes/{ordemServicoId}/preparacoes/{preparacaoId}";
        var caminhos = raiz.GetProperty("paths");
        var operacoes = new HashSet<string>();
        foreach (var caminho in caminhos.EnumerateObject())
        {
            foreach (var operacao in caminho.Value.EnumerateObject())
            {
                operacoes.Add(operacao.Value.GetProperty("operationId").GetString()!);
                Assert.True(operacao.Value.GetProperty("security")[0].TryGetProperty("Bearer", out _));
                var respostas = operacao.Value.GetProperty("responses");
                foreach (var codigo in new[] { "400", "401", "403", "404", "409", "422", "500", "503" })
                    Assert.True(respostas.GetProperty(codigo).TryGetProperty("content", out _));
                if (caminho.Name == "/api/v1/execucoes/estoque/registrar-entrada")
                {
                    Assert.True(respostas.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
                    Assert.False(respostas.TryGetProperty("202", out _));
                }
                else if (operacao.Name == "post")
                {
                    Assert.True(respostas.GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _));
                    Assert.True(respostas.GetProperty("202").GetProperty("content").TryGetProperty("application/json", out _));
                    Assert.False(respostas.TryGetProperty("200", out _));
                }
            }
        }
        Assert.Equal(5, caminhos.EnumerateObject().Count());
        Assert.True(operacoes.SetEquals(["ObterExecucao", "IniciarExecucaoServico", "FinalizarServico", "FinalizarExecucao", "RegistrarEntradaEstoque"]));
        Assert.True(caminhos.GetProperty(baseRota).GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        var acao = raiz.GetProperty("components").GetProperty("schemas").GetProperty("AcaoExecucaoRequest");
        Assert.Contains(acao.GetProperty("required").EnumerateArray(), propriedade => propriedade.GetString() == "operacaoId");
        Assert.Equal("uuid", acao.GetProperty("properties").GetProperty("operacaoId").GetProperty("format").GetString());
    }

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
