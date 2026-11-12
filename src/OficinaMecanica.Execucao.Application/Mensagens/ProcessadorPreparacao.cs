using System.Text.Json;
using OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;
using OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;

namespace OficinaMecanica.Execucao.Application.Mensagens;

public sealed class ProcessadorPreparacao(PrepararExecucaoUseCase preparar, CancelarPreparacaoUseCase cancelar,
    AutorizarExecucaoUseCase autorizar, IObservabilidadeExecucao? observabilidade = null) : IProcessadorMensagem
{
    public async Task<Mensagem?> ProcessarAsync(string json, CancellationToken cancellationToken = default)
    {
        var mensagem = InspecaoMensagemPreparacao.LerEnvelope(json);
        mensagem = mensagem with { TraceContext = ContextoTrace.Filtrar(mensagem.TraceContext) };
        using var escopo = observabilidade?.Iniciar("execucao.processar", mensagem);
        try
        {
            if (mensagem.Tipo == "AutorizarExecucao")
            {
                await autorizar.ExecuteAsync(new(mensagem, mensagem.Dados.Deserialize<AutorizarExecucao>(JsonContrato.Options)
                    ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS")), cancellationToken);
                escopo?.Concluir("autorizada");
                return null;
            }
            var resultado = mensagem.Tipo switch
            {
                "PrepararExecucao" => (await preparar.ExecuteAsync(new(mensagem, mensagem.Dados.Deserialize<OficinaMecanica.Execucao.Application.Mensagens.PrepararExecucao>(JsonContrato.Options) ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS")), cancellationToken)).Resultado,
                "CancelarPreparacao" => (await cancelar.ExecuteAsync(new(mensagem, mensagem.Dados.Deserialize<OficinaMecanica.Execucao.Application.Mensagens.CancelarPreparacao>(JsonContrato.Options) ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS")), cancellationToken)).Resultado,
                _ => throw new ContratoMensagemException("TIPO_DESCONHECIDO")
            };
            escopo?.Concluir(resultado.Tipo);
            return resultado;
        }
        catch (JsonException) { escopo?.Falhar(); throw new ContratoMensagemException("DADOS_INVALIDOS"); }
        catch { escopo?.Falhar(); throw; }
    }

}
