using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;

public sealed class IniciarExecucaoServicoValidator : AbstractValidator<IniciarExecucaoServicoRequest>
{
    public IniciarExecucaoServicoValidator()
    {
        RuleFor(request => request).Must(request => request.OperacaoId != Guid.Empty && request.OrdemServicoId != Guid.Empty
            && request.PreparacaoId != Guid.Empty && request.ServicoId != Guid.Empty).WithMessage("INICIO_SERVICO_INVALIDO");
    }
}
