namespace OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;

public sealed record IniciarExecucaoServicoRequest(Guid OperacaoId, Guid OrdemServicoId, Guid PreparacaoId, Guid ServicoId);
