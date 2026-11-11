using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

public sealed class FinalizarExecucaoValidator : AbstractValidator<FinalizarExecucaoRequest>
{
    public FinalizarExecucaoValidator()
    {
        RuleFor(request => request).Must(request => request.OperacaoId != Guid.Empty && request.OrdemServicoId != Guid.Empty
            && request.PreparacaoId != Guid.Empty).WithMessage("FINALIZACAO_EXECUCAO_INVALIDA");
    }
}
