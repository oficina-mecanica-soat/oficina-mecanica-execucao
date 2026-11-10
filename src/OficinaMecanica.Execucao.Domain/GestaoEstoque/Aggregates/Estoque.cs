using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Messages;

namespace OficinaMecanica.Execucao.Domain.GestaoEstoque.Aggregates;

public sealed class Estoque
{
    private readonly List<ItemEstoque> _itensEstoque;

    private Estoque()
    {
        _itensEstoque = new List<ItemEstoque>();
    }

    private Estoque(Guid id, IEnumerable<ItemEstoque> itensEstoque)
    {
        Id = id;
        _itensEstoque = itensEstoque.ToList();
    }

    public Guid Id { get; private set; }
    public IReadOnlyCollection<ItemEstoque> ItensEstoque => _itensEstoque.AsReadOnly();

    public static Estoque Criar(IEnumerable<ItemEstoque> itensEstoque)
    {
        var itens = itensEstoque?.ToList() ?? new List<ItemEstoque>();

        if (itens.Any(item => item is null))
        {
            throw new DomainException(EstoqueErrorMessages.EstoqueComItemNulo);
        }

        return new Estoque(Guid.NewGuid(), itens);
    }

    public static Estoque Reconstituir(Guid id, IEnumerable<ItemEstoque> itensEstoque) => new(id, itensEstoque);

    public bool VerificarDisponibilidade(Guid pecaInsumoCatalogoId, int quantidade)
    {
        var item = EncontrarItem(pecaInsumoCatalogoId);

        return item is not null && item.PossuiDisponibilidade(quantidade);
    }

    public void ReservarItens(Guid pecaInsumoCatalogoId, int quantidade)
    {
        ObterItem(pecaInsumoCatalogoId).Reservar(quantidade);
    }

    public void EstornarItens(Guid pecaInsumoCatalogoId, int quantidade)
    {
        ObterItem(pecaInsumoCatalogoId).Estornar(quantidade);
    }

    public void BaixarItens(Guid pecaInsumoCatalogoId, int quantidade)
    {
        ObterItem(pecaInsumoCatalogoId).Baixar(quantidade);
    }

    public ItemEstoque RegistrarEntrada(Guid pecaInsumoCatalogoId, int quantidade)
    {
        var item = EncontrarItem(pecaInsumoCatalogoId);

        if (item is null)
        {
            item = ItemEstoque.Criar(pecaInsumoCatalogoId, quantidade);
            _itensEstoque.Add(item);

            return item;
        }

        item.RegistrarEntrada(quantidade);

        return item;
    }

    public ItemEstoque AtualizarQuantidadeDisponivel(Guid pecaInsumoCatalogoId, int quantidadeDisponivel)
    {
        var item = ObterItem(pecaInsumoCatalogoId);

        item.AtualizarQuantidadeDisponivel(quantidadeDisponivel);

        return item;
    }

    public ItemEstoque ObterItem(Guid pecaInsumoCatalogoId)
    {
        return EncontrarItem(pecaInsumoCatalogoId)
            ?? throw new DomainException(EstoqueErrorMessages.ItemNaoEncontrado);
    }

    private ItemEstoque? EncontrarItem(Guid pecaInsumoCatalogoId)
    {
        if (pecaInsumoCatalogoId == Guid.Empty)
        {
            throw new DomainException(EstoqueErrorMessages.PecaInsumoCatalogoObrigatorio);
        }

        return _itensEstoque.SingleOrDefault(item => item.PecaInsumoCatalogoId == pecaInsumoCatalogoId);
    }
}
