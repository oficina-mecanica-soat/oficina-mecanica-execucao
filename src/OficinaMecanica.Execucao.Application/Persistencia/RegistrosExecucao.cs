using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Entities;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;

namespace OficinaMecanica.Execucao.Application.Persistencia;

public sealed record ReferenciaPreparacao(Guid OrdemServicoId, int Versao);
public sealed record PreparacaoAtual(int Versao, Guid PreparacaoId, bool Ativa);
public sealed record EstadoPreparacao(Guid PreparacaoId, PrepararExecucao Snapshot, bool Reservada, bool Cancelada,
    AutorizacaoPersistida? Autorizacao = null, CicloExecucaoPersistido? Execucao = null);
public sealed record AutorizacaoPersistida(int VersaoOrcamento, Mensagem Origem, string Hash);
public sealed record ReservaPersistida(Guid PreparacaoId, Guid FilialId, Guid ItemId, int Quantidade, bool Liberada, bool Baixada = false);

public sealed record ServicoPersistido(Guid Id, Guid ServicoCatalogoId, decimal Valor, StatusServico Status, DateTime? DataInicio, DateTime? DataFim)
{
    public Servico Reconstituir() => Servico.Reconstituir(Id, ServicoCatalogoId, Valor, Status, DataInicio, DataFim);
    public static ServicoPersistido De(Servico servico) => new(servico.Id, servico.ServicoCatalogoId, servico.Valor,
        servico.Status, servico.DataInicio, servico.DataFim);
}

public sealed record CicloExecucaoPersistido(ServicoPersistido[] Servicos, DateTime? FinalizadaEm)
{
    public Domain.GestaoOrdemServico.Aggregates.Execucao Reconstituir()
        => Domain.GestaoOrdemServico.Aggregates.Execucao.Reconstituir(Servicos.Select(servico => servico.Reconstituir()), FinalizadaEm);
    public static CicloExecucaoPersistido De(Domain.GestaoOrdemServico.Aggregates.Execucao execucao)
        => new(execucao.Servicos.Select(ServicoPersistido.De).ToArray(), execucao.FinalizadaEm);
}
