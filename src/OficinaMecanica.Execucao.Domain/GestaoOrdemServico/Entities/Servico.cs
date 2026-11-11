using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Messages;

namespace OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Entities;

public sealed class Servico
{
    private Servico()
    {
    }

    private Servico(Guid id, Guid servicoCatalogoId, decimal valor)
    {
        Id = id;
        ServicoCatalogoId = servicoCatalogoId;
        Valor = valor;
        Status = StatusServico.Pendente;
    }

    public Guid Id { get; private set; }
    public DateTime? DataInicio { get; private set; }
    public DateTime? DataFim { get; private set; }
    public StatusServico Status { get; private set; }
    public Guid ServicoCatalogoId { get; private set; }
    public decimal Valor { get; private set; }

    public static Servico Criar(Guid servicoCatalogoId, decimal valor)
    {
        if (servicoCatalogoId == Guid.Empty)
        {
            throw new DomainException(OrdemServicoErrorMessages.ServicoCatalogoObrigatorio);
        }

        if (valor <= 0)
        {
            throw new DomainException(OrdemServicoErrorMessages.ValorServicoMaiorQueZero);
        }

        return new Servico(Guid.NewGuid(), servicoCatalogoId, valor);
    }

    public static Servico Reconstituir(Guid id, Guid servicoCatalogoId, decimal valor, StatusServico status,
        DateTime? dataInicio, DateTime? dataFim)
    {
        if (id == Guid.Empty || servicoCatalogoId == Guid.Empty || valor <= 0 || !Enum.IsDefined(status)
            || (status == StatusServico.Pendente && (dataInicio is not null || dataFim is not null))
            || (status == StatusServico.EmExecucao && (dataInicio is null || dataFim is not null))
            || (status == StatusServico.Finalizado && (dataInicio is null || dataFim is null || dataFim < dataInicio))
            || (dataInicio is not null && dataInicio.Value.Kind != DateTimeKind.Utc)
            || (dataFim is not null && dataFim.Value.Kind != DateTimeKind.Utc))
            throw new DomainException("Estado persistido do serviço é inválido.");
        return new Servico(id, servicoCatalogoId, valor) { Status = status, DataInicio = dataInicio, DataFim = dataFim };
    }

    public void IniciarExecucao() => IniciarExecucao(DateTime.UtcNow);

    public void IniciarExecucao(DateTime agora)
    {
        if (Status != StatusServico.Pendente)
        {
            throw new DomainException(OrdemServicoErrorMessages.ServicoPendenteParaIniciarExecucao);
        }

        if (agora.Kind != DateTimeKind.Utc)
            throw new DomainException("O início do serviço deve usar UTC.");
        Status = StatusServico.EmExecucao;
        DataInicio = agora;
    }

    public void Finalizar() => Finalizar(DateTime.UtcNow);

    public void Finalizar(DateTime agora)
    {
        if (Status != StatusServico.EmExecucao)
        {
            throw new DomainException(OrdemServicoErrorMessages.ServicoEmExecucaoParaFinalizar);
        }

        if (agora.Kind != DateTimeKind.Utc || agora < DataInicio)
            throw new DomainException("O fim do serviço deve usar UTC e não pode preceder o início.");
        Status = StatusServico.Finalizado;
        DataFim = agora;
    }
}
