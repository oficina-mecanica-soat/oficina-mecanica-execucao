using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Application.Preparacoes;

public sealed record ExecucaoResponse(Guid PreparacaoId, int VersaoOrcamento, CicloExecucaoPersistido Execucao);
