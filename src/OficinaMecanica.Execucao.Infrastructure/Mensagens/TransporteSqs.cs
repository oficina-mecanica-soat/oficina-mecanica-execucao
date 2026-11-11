using System.Net;
using Amazon.SQS;
using Amazon.SQS.Model;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.Mensagens;

public sealed class TransporteSqs : ITransporteResultado
{
    private readonly IAmazonSQS _client;
    private readonly string _filaOsUrl;

    public TransporteSqs(IAmazonSQS client, string filaOsUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        ContratoFilaSqs.ValidarUrl(filaOsUrl);
        _client = client;
        _filaOsUrl = filaOsUrl;
    }

    public async Task PublicarAsync(Mensagem mensagem, CancellationToken cancellationToken)
    {
        if (mensagem.MensagemId == Guid.Empty || mensagem.OrdemServicoId == Guid.Empty)
            throw new ContratoMensagemException("IDENTIFICADORES_FIFO_INVALIDOS");
        var resposta = await _client.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _filaOsUrl,
            MessageBody = JsonContrato.Serializar(mensagem with { TraceContext = ContextoTrace.Filtrar(mensagem.TraceContext) }),
            MessageGroupId = mensagem.OrdemServicoId.ToString("D"),
            MessageDeduplicationId = mensagem.MensagemId.ToString("D")
        }, cancellationToken);
        if (resposta.HttpStatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(resposta.MessageId))
            throw new InvalidOperationException("Publicação SQS sem confirmação válida; a outbox deve repetir o envio.");
    }
}

internal static class ContratoFilaSqs
{
    internal static void ValidarUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.AbsolutePath.EndsWith(".fifo", StringComparison.Ordinal) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Informe a URL HTTPS de uma fila FIFO provisionada pelo owner.", nameof(url));
    }
}
