using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Aggregates;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

internal sealed class EstoqueTransacionalRepository(TransacaoExecucao tx) : IEstoqueRepository
{
    public async Task<Estoque> ObterAsync(Guid filialId, IReadOnlyCollection<Guid> itens, CancellationToken ct)
    {
        // Sete registros fixos e dois por item devem caber em uma transação.
        if (itens.Count > 46) throw new ContratoMensagemException("LIMITE_TRANSACAO_DYNAMODB");
        var saldos = new List<ItemEstoque>();
        foreach (var id in itens)
        {
            var saldo = await tx.LerAsync<SaldoEstoque>(ChavesExecucao.Estoque(filialId, id), ct);
            if (saldo is not null) saldos.Add(saldo.Reconstituir());
        }
        return Domain.GestaoEstoque.Aggregates.Estoque.Reconstituir(filialId, saldos);
    }
    public Task AtualizarAsync(Guid filialId, Estoque estoque, CancellationToken ct)
    {
        foreach (var item in estoque.ItensEstoque)
            tx.Salvar(ChavesExecucao.Estoque(filialId, item.PecaInsumoCatalogoId), SaldoEstoque.De(item));
        return Task.CompletedTask;
    }
}
