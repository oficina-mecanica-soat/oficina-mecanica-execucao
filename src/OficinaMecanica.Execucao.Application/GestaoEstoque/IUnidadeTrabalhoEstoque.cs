using OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;

namespace OficinaMecanica.Execucao.Application.GestaoEstoque;

public interface IUnidadeTrabalhoEstoque
{
    Task<T> ExecutarAsync<T>(Func<ISessaoEstoque, Task<T>> operacao, CancellationToken cancellationToken);
}

public interface ISessaoEstoque
{
    IEstoqueRepository Estoque { get; }
    Task<EntradaEstoqueRegistrada?> ObterEntradaAsync(Guid operacaoId, CancellationToken cancellationToken);
    Task RegistrarEntradaAsync(EntradaEstoqueRegistrada entrada, CancellationToken cancellationToken);
}
