using OficinaMecanica.Execucao.Domain.GestaoEstoque.Aggregates;

namespace OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;

public interface IEstoqueRepository
{
    Task<Estoque> ObterAsync(Guid filialId, IReadOnlyCollection<Guid> itens, CancellationToken ct);
    Task AtualizarAsync(Guid filialId, Estoque estoque, CancellationToken ct);
}
