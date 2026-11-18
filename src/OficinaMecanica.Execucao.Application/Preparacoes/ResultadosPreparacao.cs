using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.Preparacoes;

internal static class ResultadosPreparacao
{
    public static Mensagens.PrepararExecucao Normalizar(Mensagens.PrepararExecucao dados) => dados with
    {
        Itens = dados.Itens.OrderBy(item => item.ItemId).ToArray(),
        Servicos = dados.Servicos.OrderBy(servico => servico.ServicoId).Select(servico => servico with
        {
            Valor = decimal.Parse(servico.Valor.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
        }).ToArray()
    };

    public static string Hash<T>(Mensagem mensagem, T dados) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonContrato.Serializar(new { mensagem.Tipo, mensagem.OrdemServicoId, Dados = dados }))));

    public static Mensagem Criar<T>(Mensagem origem, string tipo, T dados, TimeProvider relogio, IObservabilidadeExecucao? observabilidade = null) => new(Guid.CreateVersion7(), tipo, 1,
        origem.SagaId, origem.OrdemServicoId, origem.CorrelationId, origem.MensagemId, relogio.GetUtcNow(),
        JsonSerializer.SerializeToElement(dados, JsonContrato.Options),
        ContextoTrace.Filtrar(observabilidade?.CapturarContexto()) ?? ContextoTrace.Filtrar(origem.TraceContext));
}

