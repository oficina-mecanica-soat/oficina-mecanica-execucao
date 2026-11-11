using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Application.Preparacoes.ObterExecucao;

public sealed record ObterExecucaoResponse(Guid OrdemServicoId, Guid PreparacaoId, int VersaoPreparacao,
    int? VersaoOrcamento, bool Reservada, bool Cancelada, CicloExecucaoPersistido? Execucao);
