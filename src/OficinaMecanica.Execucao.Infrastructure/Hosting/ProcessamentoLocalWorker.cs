using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Amazon.DynamoDBv2;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.Hosting;

public sealed class ProcessamentoLocalOptions
{
    public bool Habilitado { get; set; }
    public string Endpoint { get; set; } = "http://localhost:8000";
    public string Tabela { get; set; } = "oficina-mecanica-execucao-local";
    public string Diretorio { get; set; } = ".local/mensagens";
}

public sealed class ProcessamentoLocalWorker(IOptions<ProcessamentoLocalOptions> options,
    IHostEnvironment ambiente, ILogger<ProcessamentoLocalWorker> logger, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Habilitado)
            return;
        if (!ambiente.IsDevelopment())
            throw new InvalidOperationException("Processamento por arquivos é exclusivo do ambiente Development.");

        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<AmazonDynamoDBClient>();
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                await DynamoDbLocal.PrepararTabelaAsync(client, config.Tabela, stoppingToken);
                break;
            }
            catch (Exception) when (tentativa < 29 && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        var consumidor = scope.ServiceProvider.GetRequiredService<ConsumidorArquivos>();
        var publicador = scope.ServiceProvider.GetRequiredService<PublicadorOutbox>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumidor.ProcessarProximaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Processamento local pendente de nova tentativa; estado e mensagens preservados.");
            }
            try
            {
                await publicador.PublicarProximaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Publicação local pendente de nova tentativa; outbox preservada.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }
}
