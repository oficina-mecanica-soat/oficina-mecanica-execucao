using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.Hosting;

public sealed record ProcessamentoRemotoOptions(string Tabela, string FilaEntradaUrl, string FilaResultadosOsUrl)
{
    public void Validar()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Tabela);
        ContratoFilaSqs.ValidarUrl(FilaEntradaUrl);
        ContratoFilaSqs.ValidarUrl(FilaResultadosOsUrl);
        if (new Uri(FilaEntradaUrl) == new Uri(FilaResultadosOsUrl))
            throw new ArgumentException("A entrada do Execução não pode receber os resultados destinados à OS.");
    }
}

public sealed class ProcessamentoRemotoWorker(IServiceScopeFactory scopeFactory,
    ILogger<ProcessamentoRemotoWorker> logger, TimeProvider relogio) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var encerramento = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        // O long polling de entrada não deve impedir a publicação de resultados já confirmados.
        var consumo = ExecutarLoopAsync(true, encerramento.Token);
        var publicacao = ExecutarLoopAsync(false, encerramento.Token);
        await Task.WhenAny(consumo, publicacao);
        encerramento.Cancel();
        await Task.WhenAll(consumo, publicacao);
    }

    private async Task ExecutarLoopAsync(bool consumir, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var consumidor = consumir ? scope.ServiceProvider.GetRequiredService<ConsumidorSqs>() : null;
                var publicador = consumir ? null : scope.ServiceProvider.GetRequiredService<PublicadorOutbox>();
                var processou = false;
                try
                {
                    processou = consumir
                        ? await consumidor!.ProcessarProximaAsync(ct)
                        : await publicador!.PublicarProximaAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (SnapshotNaoPersistivelException error)
                {
                    logger.LogWarning("Rejeição técnica permanente {codigo}; mensagem {mensagem_id}, OS {ordem_servico_id}. Sem confirmação; política DLQ preservada.",
                        SnapshotNaoPersistivelException.Codigo, error.MensagemId, error.OrdemServicoId);
                }
                catch (Exception)
                {
                    // Não registrar envelope, credenciais ou detalhes sensíveis devolvidos pelo SDK.
                    if (consumir)
                        logger.LogWarning("Consumo SQS pendente de nova tentativa; entrada preservada.");
                    else
                        logger.LogWarning("Publicação SQS pendente de nova tentativa; outbox preservada.");
                }

                if (!processou)
                    await Task.Delay(TimeSpan.FromMilliseconds(500), relogio, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
