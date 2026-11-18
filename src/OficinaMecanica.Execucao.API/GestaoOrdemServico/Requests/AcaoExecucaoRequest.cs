using System.Text.Json.Serialization;

namespace OficinaMecanica.Execucao.API.GestaoOrdemServico.Requests;

public sealed record AcaoExecucaoRequest([property: JsonRequired] Guid OperacaoId);
