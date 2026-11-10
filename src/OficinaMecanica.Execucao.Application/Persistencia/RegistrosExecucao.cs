using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.Persistencia;

public sealed record ReferenciaPreparacao(Guid OrdemServicoId, int Versao);
public sealed record PreparacaoAtual(int Versao, Guid PreparacaoId, bool Ativa);
public sealed record EstadoPreparacao(Guid PreparacaoId, PrepararExecucao Snapshot, bool Reservada, bool Cancelada);
public sealed record ReservaPersistida(Guid PreparacaoId, Guid FilialId, Guid ItemId, int Quantidade, bool Liberada);
