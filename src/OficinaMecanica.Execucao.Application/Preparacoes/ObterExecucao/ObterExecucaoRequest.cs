namespace OficinaMecanica.Execucao.Application.Preparacoes.ObterExecucao;

public sealed record ObterExecucaoRequest(Guid OrdemServicoId, Guid PreparacaoId);
