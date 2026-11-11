using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.Preparacoes.ObterExecucao;

public sealed class ObterExecucaoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho, IValidator<ObterExecucaoRequest> validator)
{
    public async Task<ObterExecucaoResponse> ExecuteAsync(ObterExecucaoRequest request, CancellationToken ct = default)
    {
        var validacao = await validator.ValidateAsync(request, ct);
        if (!validacao.IsValid) throw new ContratoMensagemException(validacao.Errors[0].ErrorMessage);
        return await unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var referencia = await sessao.ObterReferenciaAsync(request.PreparacaoId, ct);
            if (referencia is null || referencia.OrdemServicoId != request.OrdemServicoId)
                throw new PreparacaoNaoEncontradaException();
            var estado = await sessao.ObterPreparacaoAsync(request.OrdemServicoId, referencia.Versao, ct);
            if (estado is null || estado.PreparacaoId != request.PreparacaoId)
                throw new PreparacaoNaoEncontradaException();
            return new ObterExecucaoResponse(request.OrdemServicoId, estado.PreparacaoId, referencia.Versao,
                estado.Autorizacao?.VersaoOrcamento, estado.Reservada, estado.Cancelada, estado.Execucao);
        }, ct);
    }
}
