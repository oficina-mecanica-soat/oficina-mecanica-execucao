using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Entities;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Aggregates;

public sealed class Execucao
{
    public IReadOnlyList<Servico> Servicos { get; }
    public DateTime? IniciadaEm => Servicos.Min(servico => servico.DataInicio);
    public DateTime? FinalizadaEm { get; private set; }

    private Execucao(Servico[] servicos, DateTime? finalizadaEm)
    {
        if (servicos.Length == 0 || servicos.Any(servico => servico is null)
            || servicos.Select(servico => servico.Id).Distinct().Count() != servicos.Length)
            throw new DomainException("Execução deve possuir serviços distintos e válidos.");
        if (finalizadaEm is not null && (finalizadaEm.Value.Kind != DateTimeKind.Utc
            || servicos.Any(servico => servico.Status != StatusServico.Finalizado || servico.DataFim > finalizadaEm)))
            throw new DomainException("Finalização persistida da execução é inválida.");
        Servicos = Array.AsReadOnly(servicos);
        FinalizadaEm = finalizadaEm;
    }

    public static Execucao Reconstituir(IEnumerable<Servico> servicos, DateTime? finalizadaEm)
        => new(servicos.ToArray(), finalizadaEm);

    public bool IniciarServico(Guid servicoId, DateTime agora)
    {
        var servico = ObterServico(servicoId);
        if (servico.Status != StatusServico.Pendente) return false;
        ExigirNaoFinalizada();
        servico.IniciarExecucao(agora);
        return true;
    }

    public bool FinalizarServico(Guid servicoId, DateTime agora)
    {
        var servico = ObterServico(servicoId);
        if (servico.Status == StatusServico.Finalizado) return false;
        ExigirNaoFinalizada();
        servico.Finalizar(agora);
        return true;
    }

    public bool Finalizar(DateTime agora)
    {
        if (FinalizadaEm is not null) return false;
        if (Servicos.Any(servico => servico.Status != StatusServico.Finalizado))
            throw new DomainException("Todos os serviços devem estar finalizados.");
        if (agora.Kind != DateTimeKind.Utc || Servicos.Any(servico => servico.DataFim > agora))
            throw new DomainException("Data de finalização da execução é inválida.");
        FinalizadaEm = agora;
        return true;
    }

    private Servico ObterServico(Guid id) => Servicos.SingleOrDefault(servico => servico.Id == id)
        ?? throw new DomainException("Serviço não encontrado na execução.");

    private void ExigirNaoFinalizada()
    {
        if (FinalizadaEm is not null) throw new DomainException("Execução já finalizada.");
    }
}
