using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Observabilidade;

namespace OficinaMecanica.Execucao.Infrastructure.UnitTests;

public sealed class ObservabilidadeDatadogTests
{
    [Fact]
    public void Dado_EscoposAninhadosSemProfiler_Quando_Encerrar_Entao_RestauraContextoSemVazarParaProximaMensagem()
    {
        var observabilidade = new ObservabilidadeDatadog(NullLogger<ObservabilidadeDatadog>.Instance);
        var primeiro = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var segundo = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-00";
        using (var externo = observabilidade.Iniciar("execucao.processar", Mensagem(primeiro)))
        {
            Assert.Equal(primeiro, observabilidade.CapturarContexto()!["traceparent"]);
            using (var interno = observabilidade.Iniciar("execucao.publicar", Mensagem(segundo)))
            {
                Assert.Equal(segundo, observabilidade.CapturarContexto()!["traceparent"]);
                interno.Falhar();
            }
            Assert.Equal(primeiro, observabilidade.CapturarContexto()!["traceparent"]);
            externo.Concluir("ExecucaoPreparada");
        }
        using var proximo = observabilidade.Iniciar("execucao.processar", Mensagem(null));
        Assert.Null(observabilidade.CapturarContexto());
        proximo.Concluir("autorizada");
    }

    private static Mensagem Mensagem(string? trace) => new(Guid.NewGuid(), "PrepararExecucao", 1, Guid.NewGuid(), Guid.NewGuid(),
        "correlacao-teste", Guid.NewGuid(), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { }),
        trace is null ? null : new Dictionary<string, string> { ["traceparent"] = trace });
}
