using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.ObterExecucao;

public sealed class ObterExecucaoValidator : AbstractValidator<ObterExecucaoRequest>
{
    public ObterExecucaoValidator()
    {
        RuleFor(request => request).Must(request => request.OrdemServicoId != Guid.Empty && request.PreparacaoId != Guid.Empty)
            .WithMessage("CONSULTA_EXECUCAO_INVALIDA");
    }
}
