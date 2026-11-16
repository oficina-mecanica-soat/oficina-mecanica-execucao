using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Observabilidade;

namespace OficinaMecanica.Execucao.Infrastructure.UnitTests;

public sealed class ObservabilidadeDatadogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dado_OperacaoDeMensagem_Quando_Encerrar_Entao_SeparaResultadoTecnicoDoResultadoDeNegocio(bool falha)
    {
        // Arrange
        var logger = new Mock<ILogger<ObservabilidadeDatadog>>();
        var observabilidade = new ObservabilidadeDatadog(logger.Object);

        // Act
        using (var escopo = observabilidade.Iniciar("execucao.processar", Mensagem(null)))
        {
            if (falha)
            {
                escopo.Falhar();
            }
            else
            {
                escopo.Concluir("ExecucaoPreparada");
            }
        }

        // Assert
        var evento = Assert.Single(logger.Invocations, i => i.Method.Name == "Log");
        Assert.Equal(falha ? LogLevel.Warning : LogLevel.Information, evento.Arguments[0]);
        var propriedades = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(evento.Arguments[2]);
        Assert.Contains(propriedades, p => p.Key == "outcome" && Equals(p.Value, falha ? "falha" : "concluida"));
        Assert.Contains(propriedades, p => p.Key == "duration_ms" && p.Value is double);
        Assert.Contains(logger.Invocations.Where(i => i.Method.Name == "BeginScope"),
            i => i.Arguments[0] is Dictionary<string, object> contexto
                && contexto.TryGetValue("resultado", out var resultado)
                && Equals(resultado, falha ? "falha" : "ExecucaoPreparada"));
    }

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
