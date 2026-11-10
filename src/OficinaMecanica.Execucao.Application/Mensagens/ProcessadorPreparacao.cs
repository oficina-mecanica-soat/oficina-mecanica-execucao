using System.Text.Json;
using OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;

namespace OficinaMecanica.Execucao.Application.Mensagens;

public sealed class ProcessadorPreparacao(PrepararExecucaoUseCase preparar, CancelarPreparacaoUseCase cancelar) : IProcessadorMensagem
{
    public async Task<Mensagem> ProcessarAsync(string json, CancellationToken cancellationToken = default)
    {
        Mensagem mensagem;
        try
        {
            mensagem = JsonContrato.Ler<Mensagem>(json);
            ValidarEnvelope(mensagem);
        }
        catch (JsonException) { throw new ContratoMensagemException("JSON_INVALIDO"); }

        try
        {
            return mensagem.Tipo switch
            {
                "PrepararExecucao" => (await preparar.ExecuteAsync(new(mensagem, mensagem.Dados.Deserialize<OficinaMecanica.Execucao.Application.Mensagens.PrepararExecucao>(JsonContrato.Options) ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS")), cancellationToken)).Resultado,
                "CancelarPreparacao" => (await cancelar.ExecuteAsync(new(mensagem, mensagem.Dados.Deserialize<OficinaMecanica.Execucao.Application.Mensagens.CancelarPreparacao>(JsonContrato.Options) ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS")), cancellationToken)).Resultado,
                _ => throw new ContratoMensagemException("TIPO_DESCONHECIDO")
            };
        }
        catch (JsonException) { throw new ContratoMensagemException("DADOS_INVALIDOS"); }
    }

    private static void ValidarEnvelope(Mensagem mensagem)
    {
        if (mensagem.Versao != 1 || mensagem.MensagemId == Guid.Empty || mensagem.SagaId == Guid.Empty
            || mensagem.OrdemServicoId == Guid.Empty || mensagem.CausationId == Guid.Empty
            || string.IsNullOrWhiteSpace(mensagem.CorrelationId) || mensagem.OcorridoEm == default
            || mensagem.OcorridoEm.Offset != TimeSpan.Zero || mensagem.Dados.ValueKind != JsonValueKind.Object)
            throw new ContratoMensagemException("ENVELOPE_INVALIDO");
    }
}
