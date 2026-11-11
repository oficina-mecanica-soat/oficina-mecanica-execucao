namespace OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

public sealed record FinalizarExecucaoRequest(Guid OperacaoId, Guid OrdemServicoId, Guid PreparacaoId);
