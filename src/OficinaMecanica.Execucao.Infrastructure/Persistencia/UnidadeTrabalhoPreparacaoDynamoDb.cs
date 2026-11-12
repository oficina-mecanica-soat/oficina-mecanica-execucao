using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Aggregates;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public sealed class UnidadeTrabalhoPreparacaoDynamoDb(IArmazenamentoExecucao armazenamento) : IUnidadeTrabalhoPreparacao
{
    public async Task<T> ExecutarAsync<T>(Func<ISessaoPreparacao, Task<T>> operacao, CancellationToken cancellationToken)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            var tx = new TransacaoExecucao(armazenamento);
            try
            {
                T resultado;
                try
                {
                    resultado = await operacao(new SessaoPreparacao(tx));
                }
                catch (Exception error) when (error is DomainException or ContratoMensagemException or PreparacaoNaoEncontradaException)
                {
                    // Leituras individuais não formam um snapshot. Antes de propagar a recusa,
                    // conferir as revisões sem gravar alterações parciais desta tentativa.
                    await tx.ValidarLeiturasAsync(cancellationToken);
                    throw;
                }
                await tx.ConfirmarAsync(cancellationToken);
                return resultado;
            }
            catch (ConflitoConcorrenciaException) when (tentativa < 7)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5 * (tentativa + 1)), cancellationToken);
            }
        }
    }
}

internal sealed class SessaoPreparacao(TransacaoExecucao tx) : ISessaoPreparacao, IEstoqueRepository
{
    public IEstoqueRepository Estoque => this;
    public Task<PreparacaoAtual?> ObterAtualAsync(Guid osId, CancellationToken ct) => tx.LerAsync<PreparacaoAtual>(ChavesExecucao.Atual(osId), ct);
    public Task<ReferenciaPreparacao?> ObterReferenciaAsync(Guid id, CancellationToken ct) => tx.LerAsync<ReferenciaPreparacao>(ChavesExecucao.Referencia(id), ct);
    public Task<EstadoPreparacao?> ObterPreparacaoAsync(Guid osId, int versao, CancellationToken ct) => tx.LerAsync<EstadoPreparacao>(ChavesExecucao.Preparacao(osId, versao), ct);
    public Task<ReservaPersistida?> ObterReservaAsync(Guid osId, Guid itemId, int versao, CancellationToken ct) => tx.LerAsync<ReservaPersistida>(ChavesExecucao.Reserva(osId, itemId, versao), ct);
    public Task SalvarAtualAsync(Guid osId, PreparacaoAtual atual, CancellationToken ct) { tx.Salvar(ChavesExecucao.Atual(osId), atual); return Task.CompletedTask; }
    public Task SalvarPreparacaoAsync(Guid osId, int versao, EstadoPreparacao estado, CancellationToken ct) { tx.Salvar(ChavesExecucao.Preparacao(osId, versao), estado); return Task.CompletedTask; }
    public Task AdicionarPreparacaoAsync(Guid osId, int versao, EstadoPreparacao estado, CancellationToken ct) => tx.AdicionarAsync(ChavesExecucao.Preparacao(osId, versao), estado, ct);
    public Task AdicionarReferenciaAsync(Guid id, ReferenciaPreparacao referencia, CancellationToken ct) => tx.AdicionarAsync(ChavesExecucao.Referencia(id), referencia, ct);
    public Task SalvarReservaAsync(Guid osId, int versao, ReservaPersistida reserva, bool nova, CancellationToken ct)
    {
        var chave = ChavesExecucao.Reserva(osId, reserva.ItemId, versao);
        if (nova) return tx.AdicionarAsync(chave, reserva, ct);
        tx.Salvar(chave, reserva);
        return Task.CompletedTask;
    }
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
    public async Task<Mensagem?> ObterResultadoAsync(Mensagem mensagem, int versao, string hash, CancellationToken ct)
    {
        var operacao = ChavesExecucao.Operacao(mensagem.OrdemServicoId, versao, mensagem.Tipo);
        var recebidaKey = ChavesExecucao.Recebida(mensagem.MensagemId);
        await ValidarRecebimentoAsync(mensagem.MensagemId, hash, ct);
        var existente = await tx.LerAsync<OperacaoProcessada>(operacao, ct);
        if (existente is null)
            return null;
        if (existente.Hash != hash)
            throw new ContratoMensagemException("SNAPSHOT_DIVERGENTE");
        tx.Salvar(recebidaKey, new MensagemRecebida(hash));
        return existente.Resultado;
    }

    public async Task RegistrarRecebimentoAsync(Guid identificador, string hash, CancellationToken ct)
    {
        await ValidarRecebimentoAsync(identificador, hash, ct);
        tx.Salvar(ChavesExecucao.Recebida(identificador), new MensagemRecebida(hash));
    }

    private async Task ValidarRecebimentoAsync(Guid identificador, string hash, CancellationToken ct)
    {
        var recebida = await tx.LerAsync<MensagemRecebida>(ChavesExecucao.Recebida(identificador), ct);
        if (recebida is not null && recebida.Hash != hash)
            throw new ContratoMensagemException("MENSAGEM_ID_REUTILIZADO");
    }

    public async Task RegistrarResultadoAsync(Mensagem mensagem, int versao,
        string hash, Mensagem resultado, CancellationToken ct)
    {
        var operacao = ChavesExecucao.Operacao(mensagem.OrdemServicoId, versao, mensagem.Tipo);
        tx.Salvar(ChavesExecucao.Recebida(mensagem.MensagemId), new MensagemRecebida(hash));
        tx.Salvar(operacao, new OperacaoProcessada(hash, resultado));
        await AdicionarResultadoAsync(resultado, ct);
    }

    public async Task AdicionarResultadoAsync(Mensagem resultado, CancellationToken ct)
    {
        var sequencia = await tx.LerAsync<SequenciaOutbox>(ChavesExecucao.SequenciaOutbox, ct);
        var proxima = checked((sequencia?.Valor ?? 0) + 1);
        tx.Salvar(ChavesExecucao.SequenciaOutbox, new SequenciaOutbox(proxima));
        await tx.AdicionarAsync(ChavesExecucao.Outbox(proxima, resultado), new PendenciaOutbox(resultado), ct);
    }
}

