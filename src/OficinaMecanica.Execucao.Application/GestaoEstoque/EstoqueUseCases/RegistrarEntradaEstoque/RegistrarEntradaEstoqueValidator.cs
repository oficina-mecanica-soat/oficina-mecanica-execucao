using FluentValidation;

namespace OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;

public sealed class RegistrarEntradaEstoqueValidator : AbstractValidator<RegistrarEntradaEstoqueRequest>
{
    public RegistrarEntradaEstoqueValidator()
    {
        RuleFor(request => request.OperacaoId).NotEmpty();
        RuleFor(request => request.FilialId).NotEmpty();
        RuleFor(request => request.PecaInsumoCatalogoId).NotEmpty();
        RuleFor(request => request.Quantidade).GreaterThan(0);
    }
}
