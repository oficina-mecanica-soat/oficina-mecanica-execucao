namespace OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.Responses;

public sealed record ItemEstoqueResponse(Guid Id, Guid FilialId, Guid PecaInsumoCatalogoId,
    int QuantidadeDisponivel, int QuantidadeReservada);
