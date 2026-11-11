using OficinaMecanica.Execucao.Application.Preparacoes;

namespace OficinaMecanica.Execucao.API.GestaoOrdemServico.Responses;

public sealed record OperacaoExecucaoAceitaResponse(Guid OperacaoId, ExecucaoResponse Resultado);
