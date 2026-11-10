using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;

public sealed class CancelarPreparacaoValidator : AbstractValidator<CancelarPreparacaoRequest>
{
    public CancelarPreparacaoValidator()
    {
        RuleFor(request => request.Dados).Must(dados => dados is not null && dados.PreparacaoId != Guid.Empty && !string.IsNullOrWhiteSpace(dados.Motivo)).WithMessage("CANCELAMENTO_INVALIDO");
    }
}
