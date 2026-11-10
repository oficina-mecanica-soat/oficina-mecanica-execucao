using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;

namespace OficinaMecanica.Execucao.Application.Preparacoes;

public interface IUnidadeTrabalhoPreparacao
{
    Task<T> ExecutarAsync<T>(Func<ISessaoPreparacao, Task<T>> operacao, CancellationToken cancellationToken);
}

public interface ISessaoPreparacao
{
    IEstoqueRepository Estoque { get; }
    Task<Mensagem?> ObterResultadoAsync(Mensagem mensagem, int versao, string hash, CancellationToken ct);
    Task<PreparacaoAtual?> ObterAtualAsync(Guid osId, CancellationToken ct);
    Task<ReferenciaPreparacao?> ObterReferenciaAsync(Guid preparacaoId, CancellationToken ct);
    Task<EstadoPreparacao?> ObterPreparacaoAsync(Guid osId, int versao, CancellationToken ct);
    Task<ReservaPersistida?> ObterReservaAsync(Guid osId, Guid itemId, int versao, CancellationToken ct);
    Task SalvarAtualAsync(Guid osId, PreparacaoAtual atual, CancellationToken ct);
    Task SalvarPreparacaoAsync(Guid osId, int versao, EstadoPreparacao preparacao, CancellationToken ct);
    Task AdicionarPreparacaoAsync(Guid osId, int versao, EstadoPreparacao preparacao, CancellationToken ct);
    Task AdicionarReferenciaAsync(Guid preparacaoId, ReferenciaPreparacao referencia, CancellationToken ct);
    Task SalvarReservaAsync(Guid osId, int versao, ReservaPersistida reserva, bool nova, CancellationToken ct);
    Task RegistrarResultadoAsync(Mensagem mensagem, int versao, string hash, Mensagem resultado, CancellationToken ct);
}
