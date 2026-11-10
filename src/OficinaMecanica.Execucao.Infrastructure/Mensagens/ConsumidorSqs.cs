using Amazon.SQS;
using Amazon.SQS.Model;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.Mensagens;

public sealed class ConsumidorSqs
{
    private readonly IAmazonSQS _client;
    private readonly IProcessadorMensagem _processador;
    private readonly string _filaEntradaUrl;
    private readonly TimeProvider _relogio;

    public ConsumidorSqs(IAmazonSQS client, IProcessadorMensagem processador, string filaEntradaUrl, TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(processador);
        ArgumentNullException.ThrowIfNull(relogio);
        ContratoFilaSqs.ValidarUrl(filaEntradaUrl);
        _client = client;
        _processador = processador;
        _filaEntradaUrl = filaEntradaUrl;
        _relogio = relogio;
    }

    public async Task<bool> ProcessarProximaAsync(CancellationToken cancellationToken = default)
    {
        var resposta = await _client.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _filaEntradaUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 20,
            VisibilityTimeout = 60,
            MessageSystemAttributeNames = ["MessageGroupId", "MessageDeduplicationId"]
        }, cancellationToken);
        var recebida = resposta.Messages?.SingleOrDefault();
        if (recebida is null) return false;
        if (string.IsNullOrWhiteSpace(recebida.ReceiptHandle))
            throw new ContratoMensagemException("RECIBO_SQS_AUSENTE");

        using var processamentoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processamento = ProcessarAsync(recebida, processamentoCts.Token);
        var renovacao = RenovarVisibilidadeAsync(recebida.ReceiptHandle, processamento, processamentoCts.Token);
        try
        {
            if (await Task.WhenAny(processamento, renovacao) == renovacao) await renovacao;
            // O processador só conclui após confirmar a transação de estado/deduplicação/outbox.
            await processamento;
            processamentoCts.Cancel();
            await renovacao;
            cancellationToken.ThrowIfCancellationRequested();
            await _client.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = _filaEntradaUrl,
                ReceiptHandle = recebida.ReceiptHandle
            }, cancellationToken);
            return true;
        }
        finally
        {
            processamentoCts.Cancel();
            // Observa e encerra ambas as tarefas sem esconder a falha original nem confirmar a entrada.
            try { await processamento; } catch { }
            try { await renovacao; } catch { }
        }
    }

    private async Task ProcessarAsync(Message recebida, CancellationToken ct)
    {
        var envelope = JsonContrato.Ler<Mensagem>(recebida.Body);
        if (!Guid.TryParse(recebida.Attributes?.GetValueOrDefault("MessageGroupId"), out var grupo) || grupo != envelope.OrdemServicoId
            || !Guid.TryParse(recebida.Attributes?.GetValueOrDefault("MessageDeduplicationId"), out var deduplicacao) || deduplicacao != envelope.MensagemId)
            throw new ContratoMensagemException("METADADOS_FIFO_DIVERGENTES");
        await _processador.ProcessarAsync(recebida.Body, ct);
    }

    private async Task RenovarVisibilidadeAsync(string recibo, Task processamento, CancellationToken ct)
    {
        try
        {
            while (!processamento.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), _relogio, ct);
                if (processamento.IsCompleted) return;
                await _client.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
                {
                    QueueUrl = _filaEntradaUrl,
                    ReceiptHandle = recibo,
                    VisibilityTimeout = 60
                }, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
