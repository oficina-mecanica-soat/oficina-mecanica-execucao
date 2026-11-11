using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;

public sealed class AutorizarExecucaoValidator : AbstractValidator<AutorizarExecucaoRequest>
{
    public AutorizarExecucaoValidator()
    {
        RuleFor(request => request.Dados).Must(dados => dados is not null
            && dados.PreparacaoId != Guid.Empty && dados.VersaoOrcamento > 0).WithMessage("AUTORIZACAO_INVALIDA");
    }
}
