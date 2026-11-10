using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.API;

public sealed class ProcessamentoLocalOptions
{
    public bool Habilitado { get; set; }
    public string Endpoint { get; set; } = "http://localhost:8000";
    public string Tabela { get; set; } = "oficina-mecanica-execucao-local";
    public string Diretorio { get; set; } = ".local/mensagens";
}

public sealed class ProcessamentoLocalWorker(IOptions<ProcessamentoLocalOptions> options,
    IHostEnvironment ambiente, ILogger<ProcessamentoLocalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Habilitado)
            return;
        if (!ambiente.IsDevelopment())
            throw new InvalidOperationException("Processamento por arquivos é exclusivo do ambiente Development.");

        using var client = DynamoDbLocal.CriarClient(config.Endpoint);
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
        var armazenamento = new ArmazenamentoDynamoDb(client, config.Tabela);
        var processador = new ProcessadorPreparacao(armazenamento, TimeProvider.System);
        var consumidor = new ConsumidorArquivos(processador, config.Diretorio);
        var publicador = new PublicadorOutbox(armazenamento,
            new TransporteArquivos(Path.Combine(config.Diretorio, "saida")), TimeProvider.System);

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
