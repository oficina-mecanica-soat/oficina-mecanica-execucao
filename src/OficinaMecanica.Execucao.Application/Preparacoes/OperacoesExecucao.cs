using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Application.Preparacoes;

internal static class OperacoesExecucao
{
    public static async Task<(ReferenciaPreparacao Referencia, EstadoPreparacao Estado,
        Domain.GestaoOrdemServico.Aggregates.Execucao Execucao)> CarregarAsync(ISessaoPreparacao sessao,
        Guid osId, Guid preparacaoId, CancellationToken ct)
    {
        var referencia = await sessao.ObterReferenciaAsync(preparacaoId, ct) ?? throw new PreparacaoNaoEncontradaException();
        if (referencia.OrdemServicoId != osId) throw new ContratoMensagemException("PREPARACAO_DE_OUTRA_OS");
        var estado = await sessao.ObterPreparacaoAsync(osId, referencia.Versao, ct) ?? throw new PreparacaoNaoEncontradaException();
        var atual = await sessao.ObterAtualAsync(osId, ct);
        if (estado.PreparacaoId != preparacaoId || atual?.PreparacaoId != preparacaoId || !atual.Ativa
            || estado.Cancelada || !estado.Reservada)
            throw new ContratoMensagemException("PREPARACAO_NAO_EXECUTAVEL");
        if (estado.Autorizacao is null || estado.Execucao is null)
            throw new ContratoMensagemException("EXECUCAO_NAO_AUTORIZADA");
        return (referencia, estado, estado.Execucao.Reconstituir());
    }

    public static string HashAcao<T>(string tipo, T request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonContrato.Serializar(new { Tipo = tipo, Dados = request }))));

    public static Mensagem Evento<T>(EstadoPreparacao estado, Guid acaoId, string tipo, T dados, TimeProvider relogio)
    {
        var origem = estado.Autorizacao!.Origem;
        return new(Guid.CreateVersion7(), tipo, 1, origem.SagaId, origem.OrdemServicoId, origem.CorrelationId,
            acaoId, relogio.GetUtcNow(), JsonSerializer.SerializeToElement(dados, JsonContrato.Options));
    }

    public static ExecucaoResponse Resposta(EstadoPreparacao estado, Domain.GestaoOrdemServico.Aggregates.Execucao execucao)
        => new(estado.PreparacaoId, estado.Autorizacao!.VersaoOrcamento, CicloExecucaoPersistido.De(execucao));
}
