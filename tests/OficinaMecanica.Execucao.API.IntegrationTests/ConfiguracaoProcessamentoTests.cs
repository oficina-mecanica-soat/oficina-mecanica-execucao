using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using OficinaMecanica.Execucao.API.Extensions.Configuration;
using OficinaMecanica.Execucao.API.Filters;
using OficinaMecanica.Execucao.Infrastructure.Hosting;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class ConfiguracaoProcessamentoTests
{
    [Theory]
    [InlineData(true, "Production", false, true)]
    [InlineData(false, "Production", true, false)]
    [InlineData(false, "Development", true, true)]
    [InlineData(false, "Development", false, false)]
    public async Task Dado_ModoConfigurado_Quando_VerificarDisponibilidade_Entao_RespeitaRemotoOuBarreiraLocal(
        bool remoto, string ambiente, bool local, bool esperado)
    {
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(ambiente);
        var filtro = new DisponibilidadeProcessamentoFilter(host.Object,
            Options.Create(new ProcessamentoLocalOptions { Habilitado = local }), new(remoto));
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var contexto = new ResourceExecutingContext(action, [], []);
        var executou = false;
        await filtro.OnResourceExecutionAsync(contexto, () =>
        {
            executou = true;
            return Task.FromResult(new ResourceExecutedContext(action, []));
        });
        Assert.Equal(esperado, executou);
        if (!esperado) Assert.Equal(503, Assert.IsType<ObjectResult>(contexto.Result).StatusCode);
    }

    [Fact]
    public async Task Dado_RemotoDesabilitado_Quando_Configurar_Entao_PreservaComposicaoLocalSemClientesAws()
    {
        var builder = Criar(new() { ["ProcessamentoRemoto:Habilitado"] = "false" });
        Assert.Null(await builder.AddApiProcessingAsync());
        using var provider = builder.Services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<ProcessamentoConfigurado>().Remoto);
        Assert.IsType<ProcessamentoLocalWorker>(Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public async Task Dado_ModosSimultaneos_Quando_Configurar_Entao_BloqueiaAntesDaAutenticacaoAws()
    {
        var builder = Criar(new() { ["ProcessamentoRemoto:Habilitado"] = "true", ["ProcessamentoLocal:Habilitado"] = "true" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.AddApiProcessingAsync());
        Assert.DoesNotContain(builder.Services, s => s.ServiceType == typeof(ProcessamentoConfigurado));
    }

    [Theory]
    [InlineData("ContaEsperada", "")]
    [InlineData("ContaEsperada", "invalida")]
    [InlineData("Regiao", "")]
    [InlineData("Regiao", "regiao-inexistente")]
    [InlineData("Tabela", "")]
    [InlineData("FilaEntradaUrl", "")]
    [InlineData("FilaEntradaUrl", "https://sqs.us-east-1.amazonaws.com/999999999999/entrada.fifo")]
    [InlineData("FilaEntradaUrl", "https://sqs.us-west-2.amazonaws.com/123456789012/entrada.fifo")]
    [InlineData("FilaEntradaUrl", "https://example.invalid/123456789012/entrada.fifo")]
    [InlineData("FilaEntradaUrl", "https://sqs.us-east-1.amazonaws.com:8443/123456789012/entrada.fifo")]
    [InlineData("FilaEntradaUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/extra/entrada.fifo")]
    [InlineData("FilaResultadosOsUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/entrada.fifo")]
    [InlineData("FilaResultadosOsUrl", "https://sqs.us-east-1.amazonaws.com/999999999999/resultados.fifo")]
    public async Task Dado_ContratoInvalido_Quando_Configurar_Entao_FalhaSemRegistrarModoRemoto(string chave, string valor)
    {
        var config = ConfiguracaoValida();
        config["ProcessamentoRemoto:" + chave] = valor;
        var builder = Criar(config);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => builder.AddApiProcessingAsync());
        Assert.DoesNotContain(builder.Services, s => s.ServiceType == typeof(ProcessamentoConfigurado));
    }

    private static WebApplicationBuilder Criar(Dictionary<string, string?> config)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(config);
        return builder;
    }

    private static Dictionary<string, string?> ConfiguracaoValida() => new()
    {
        ["ProcessamentoRemoto:Habilitado"] = "true",
        ["ProcessamentoRemoto:ContaEsperada"] = "123456789012",
        ["ProcessamentoRemoto:Regiao"] = "us-east-1",
        ["ProcessamentoRemoto:Tabela"] = "tabela-teste",
        ["ProcessamentoRemoto:FilaEntradaUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/entrada.fifo",
        ["ProcessamentoRemoto:FilaResultadosOsUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/resultados.fifo"
    };
}
