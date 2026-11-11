using System.Text.Json;
using System.Text.Json.Serialization;

namespace OficinaMecanica.Execucao.Application.Mensagens;

public sealed record Mensagem(Guid MensagemId, string Tipo, int Versao, Guid SagaId,
    Guid OrdemServicoId, string CorrelationId, Guid CausationId, DateTimeOffset OcorridoEm, JsonElement Dados);
public sealed record PrepararExecucao(int Versao, Guid FilialId, Guid VeiculoId, Guid MecanicoId,
    ItemSolicitado[] Itens, ServicoSolicitado[] Servicos);
public sealed record ItemSolicitado(Guid ItemId, int Quantidade);
public sealed record ServicoSolicitado(Guid ServicoId, string Descricao, decimal Valor, Guid ServicoCatalogoId);
public sealed record CancelarPreparacao(Guid PreparacaoId, string Motivo);
public sealed record AutorizarExecucao(Guid PreparacaoId, int VersaoOrcamento);
public sealed record Diagnostico(Guid VeiculoId, Guid MecanicoId, ServicoSolicitado[] Servicos);
public sealed record ExecucaoPreparada(Guid PreparacaoId, int Versao, ItemSolicitado[] ItensReservados, Diagnostico Diagnostico);
public sealed record PreparacaoRecusada(int Versao, string CodigoMotivo);
public sealed record PreparacaoCancelada(Guid PreparacaoId, int Versao);
public sealed record ExecucaoIniciada(Guid PreparacaoId, int VersaoOrcamento);
public sealed record ExecucaoFinalizada(Guid PreparacaoId, int VersaoOrcamento, DateTimeOffset FinalizadoEm);

public static class JsonContrato
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Options);
    public static T Ler<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new ContratoMensagemException("JSON_NULO");
}

public sealed class ContratoMensagemException(string codigo) : Exception(codigo);
public sealed class ConflitoConcorrenciaException : Exception;
public sealed class PreparacaoNaoEncontradaException : Exception;
