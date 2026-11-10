using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Messages;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Preparacoes;

/// <summary>
/// Reserva local de uma preparação. O chamador deve persistir preparação e saldos
/// na mesma transação; validação em memória não substitui condições no banco.
/// </summary>
public sealed class PreparacaoEstoque
{
    public Guid PreparacaoId { get; }
    public Guid OrdemServicoId { get; }
    public Guid FilialId { get; }
    public IReadOnlyList<ItemReserva> Itens { get; }
    public bool Cancelada { get; private set; }

    private PreparacaoEstoque(Guid preparacaoId, Guid ordemServicoId, Guid filialId, ItemReserva[] itens)
    {
        PreparacaoId = preparacaoId;
        OrdemServicoId = ordemServicoId;
        FilialId = filialId;
        Itens = Array.AsReadOnly(itens);
    }

    public static PreparacaoEstoque Reconstituir(Guid preparacaoId, Guid ordemServicoId, Guid filialId,
        IEnumerable<ItemReserva> itens, bool cancelada)
    {
        ValidarIdentificadores(preparacaoId, ordemServicoId, filialId);
        ArgumentNullException.ThrowIfNull(itens);
        return new PreparacaoEstoque(preparacaoId, ordemServicoId, filialId, itens.ToArray()) { Cancelada = cancelada };
    }

    public static PreparacaoEstoque Reservar(Guid preparacaoId, Guid ordemServicoId, Guid filialId,
        IEnumerable<ItemReserva> itens, IReadOnlyDictionary<Guid, ItemEstoque> estoqueDaFilial)
    {
        ValidarIdentificadores(preparacaoId, ordemServicoId, filialId);
        ArgumentNullException.ThrowIfNull(itens);
        ArgumentNullException.ThrowIfNull(estoqueDaFilial);
        var solicitados = itens.ToArray();
        if (solicitados.Any(item => item is null))
            throw new DomainException(EstoqueErrorMessages.EstoqueComItemNulo);
        if (solicitados.Select(item => item.ItemId).Distinct().Count() != solicitados.Length)
            throw new DomainException("Peças e insumos não podem conter itens repetidos.");

        // Valida todos os itens antes de alterar qualquer saldo, como na abertura da OS.
        foreach (var item in solicitados)
        {
            if (!estoqueDaFilial.TryGetValue(item.ItemId, out var saldo)
                || !saldo.PossuiDisponibilidade(item.Quantidade))
                throw new DomainException(EstoqueErrorMessages.EstoqueInsuficiente);
            if (saldo.QuantidadeReservada > int.MaxValue - item.Quantidade)
                throw new DomainException("Quantidade reservada excede o limite suportado.");
        }

        foreach (var item in solicitados)
            estoqueDaFilial[item.ItemId].Reservar(item.Quantidade);

        return new PreparacaoEstoque(preparacaoId, ordemServicoId, filialId, solicitados);
    }

    public bool Cancelar(IReadOnlyDictionary<Guid, ItemEstoque> estoqueDaFilial)
    {
        ArgumentNullException.ThrowIfNull(estoqueDaFilial);
        if (Cancelada)
            return false;

        // Falha em qualquer item impede liberação parcial e mantém a compensação pendente.
        foreach (var item in Itens)
        {
            if (!estoqueDaFilial.TryGetValue(item.ItemId, out var saldo))
                throw new DomainException(EstoqueErrorMessages.ItemNaoEncontrado);
            if (saldo.QuantidadeReservada < item.Quantidade)
                throw new DomainException(EstoqueErrorMessages.QuantidadeReservadaInsuficiente);
            if (saldo.QuantidadeDisponivel > int.MaxValue - item.Quantidade)
                throw new DomainException("Quantidade disponível excede o limite suportado.");
        }

        foreach (var item in Itens)
            estoqueDaFilial[item.ItemId].Estornar(item.Quantidade);

        Cancelada = true;
        return true;
    }

    private static void ValidarIdentificadores(Guid preparacaoId, Guid ordemServicoId, Guid filialId)
    {
        if (preparacaoId == Guid.Empty || ordemServicoId == Guid.Empty || filialId == Guid.Empty)
            throw new DomainException("Preparação, ordem de serviço e filial são obrigatórias.");
    }
}
