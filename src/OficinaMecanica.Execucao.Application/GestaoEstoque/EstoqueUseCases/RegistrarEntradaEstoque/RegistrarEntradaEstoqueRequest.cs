namespace OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;

public sealed record RegistrarEntradaEstoqueRequest(Guid OperacaoId, Guid FilialId,
    Guid PecaInsumoCatalogoId, int Quantidade);
