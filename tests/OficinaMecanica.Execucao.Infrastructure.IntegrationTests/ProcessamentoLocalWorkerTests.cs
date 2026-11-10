using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class ProcessamentoLocalWorkerTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Fact]
    public async Task Dado_EntradaPendente_Quando_PublicarOutbox_Entao_NaoBloqueiaResultadoJaConfirmado()
    {
        using var pasta = new PastaTemporaria();
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        Directory.CreateDirectory(Path.Combine(pasta.Caminho, "entrada"));
        var arquivo = Path.Combine(pasta.Caminho, "entrada", "cancelamento-pendente.json");
        await File.WriteAllTextAsync(arquivo, JsonContrato.Serializar(cenario.Comando("CancelarPreparacao", new CancelarPreparacao(Guid.NewGuid(), "Recusa"))));
        using var provider = CriarProvider(cenario, pasta);
        using var worker = new ProcessamentoLocalWorker(Options.Create(new ProcessamentoLocalOptions
        {
            Habilitado = true,
            Endpoint = fixture.Endpoint,
            Tabela = cenario.Tabela,
            Diretorio = pasta.Caminho
        }), new AmbienteTeste(), NullLogger<ProcessamentoLocalWorker>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
        await worker.StartAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!Directory.Exists(Path.Combine(pasta.Caminho, "saida"))) await Task.Delay(50, timeout.Token);
            while (Directory.GetFiles(Path.Combine(pasta.Caminho, "saida"), "*.json").Length == 0) await Task.Delay(50, timeout.Token);
        }
        finally { await worker.StopAsync(default); }

        Assert.True(File.Exists(arquivo));
        Assert.Single(Directory.GetFiles(Path.Combine(pasta.Caminho, "saida"), "*.json"));
    }

    [Fact]
    public async Task Dado_WorkerLocal_Quando_ProcessarArquivo_Entao_GravaResultadoEEncerra()
    {
        using var pasta = new PastaTemporaria();
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        Directory.CreateDirectory(Path.Combine(pasta.Caminho, "entrada"));
        await File.WriteAllTextAsync(Path.Combine(pasta.Caminho, "entrada", "preparar.json"),
            JsonContrato.Serializar(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] })));
        var options = Options.Create(new ProcessamentoLocalOptions
        {
            Habilitado = true,
            Endpoint = fixture.Endpoint,
            Tabela = cenario.Tabela,
            Diretorio = pasta.Caminho
        });
        using var provider = CriarProvider(cenario, pasta);
        using var worker = new ProcessamentoLocalWorker(options, new AmbienteTeste(), NullLogger<ProcessamentoLocalWorker>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
        await worker.StartAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!Directory.Exists(Path.Combine(pasta.Caminho, "saida")) || Directory.GetFiles(Path.Combine(pasta.Caminho, "saida"), "*.json").Length == 0)
                await Task.Delay(50, timeout.Token);
        }
        finally { await worker.StopAsync(default); }

        Assert.Single(Directory.GetFiles(Path.Combine(pasta.Caminho, "saida"), "*.json"));
        Assert.Single(Directory.GetFiles(Path.Combine(pasta.Caminho, "processadas"), "*.json"));
    }

    [Fact]
    public async Task Dado_AmbienteProduction_Quando_HabilitarArquivos_Entao_RecusaModoLocal()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var worker = new ProcessamentoLocalWorker(Options.Create(new ProcessamentoLocalOptions { Habilitado = true }),
            new AmbienteTeste { EnvironmentName = Environments.Production }, NullLogger<ProcessamentoLocalWorker>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
        await worker.StartAsync(default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);
    }
    private ServiceProvider CriarProvider(CenarioPreparacao cenario, PastaTemporaria pasta)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProcessamentoLocal:Endpoint"] = fixture.Endpoint,
            ["ProcessamentoLocal:Tabela"] = cenario.Tabela,
            ["ProcessamentoLocal:Diretorio"] = pasta.Caminho
        }).Build();
        return new ServiceCollection().AddApplication().AddInfrastructure(configuration).BuildServiceProvider();
    }

}

internal sealed class AmbienteTeste : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Teste";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
