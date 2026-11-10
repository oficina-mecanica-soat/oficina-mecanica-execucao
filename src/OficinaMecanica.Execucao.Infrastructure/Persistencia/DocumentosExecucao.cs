using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public sealed record SaldoEstoque(Guid Id, Guid ItemId, int Disponivel, int Reservada)
{
    public ItemEstoque Reconstituir() => ItemEstoque.Reconstituir(Id, ItemId, Disponivel, Reservada);
    public static SaldoEstoque De(ItemEstoque item) => new(item.Id, item.PecaInsumoCatalogoId, item.QuantidadeDisponivel, item.QuantidadeReservada);
}
public sealed record SequenciaOutbox(long Valor);
public sealed record PendenciaOutbox(Mensagem Mensagem, int Tentativas = 0, DateTimeOffset? ProximaTentativaEm = null,
    string? Responsavel = null, DateTimeOffset? LeaseAte = null, string? UltimoErro = null);

public static class ChavesExecucao
{
    public static ChaveRegistro Estoque(Guid filialId, Guid itemId) => new($"ESTOQUE#{filialId:D}", $"ITEM#{itemId:D}");
    public static ChaveRegistro Atual(Guid osId) => new($"OS#{osId:D}", "ATUAL");
    public static ChaveRegistro Preparacao(Guid osId, int versao) => new($"OS#{osId:D}", $"PREPARACAO#{versao:D10}");
    public static ChaveRegistro Reserva(Guid osId, Guid itemId, int versao) => new($"OS#{osId:D}", $"RESERVA#{itemId:D}#{versao:D10}");
    public static ChaveRegistro Referencia(Guid preparacaoId) => new($"PREPARACAO#{preparacaoId:D}", "REFERENCIA");
    public static ChaveRegistro Operacao(Guid osId, int versao, string tipo) => new($"OS#{osId:D}", $"OPERACAO#{tipo}#{versao:D10}");
    public static ChaveRegistro Recebida(Guid mensagemId) => new($"MENSAGEM#{mensagemId:D}", "RECEBIDA");
    public static ChaveRegistro SequenciaOutbox { get; } = new("OUTBOX#CONTROLE", "SEQUENCIA");
    public static ChaveRegistro Outbox(long sequencia, Mensagem mensagem) => new("OUTBOX#PENDENTE", $"{sequencia:D20}#{mensagem.MensagemId:D}");
}

public sealed record OperacaoProcessada(string Hash, Mensagem Resultado);
public sealed record MensagemRecebida(string Hash);
