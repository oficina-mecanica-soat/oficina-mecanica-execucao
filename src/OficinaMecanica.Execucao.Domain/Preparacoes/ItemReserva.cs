using OficinaMecanica.Execucao.Domain.GestaoEstoque.Messages;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Preparacoes;

public sealed record ItemReserva
{
    public Guid ItemId { get; }
    public int Quantidade { get; }

    public ItemReserva(Guid itemId, int quantidade)
    {
        if (itemId == Guid.Empty)
            throw new DomainException(EstoqueErrorMessages.PecaInsumoCatalogoObrigatorio);
        if (quantidade <= 0)
            throw new DomainException(EstoqueErrorMessages.QuantidadeMaiorQueZero);

        ItemId = itemId;
        Quantidade = quantidade;
    }
}
