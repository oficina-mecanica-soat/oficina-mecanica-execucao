namespace OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;

public sealed record FinalizarServicoRequest(Guid OperacaoId, Guid OrdemServicoId, Guid PreparacaoId, Guid ServicoId);
