using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;

public sealed class FinalizarServicoValidator : AbstractValidator<FinalizarServicoRequest>
{
    public FinalizarServicoValidator()
    {
        RuleFor(request => request).Must(request => request.OperacaoId != Guid.Empty && request.OrdemServicoId != Guid.Empty
            && request.PreparacaoId != Guid.Empty && request.ServicoId != Guid.Empty).WithMessage("FINALIZACAO_SERVICO_INVALIDA");
    }
}
