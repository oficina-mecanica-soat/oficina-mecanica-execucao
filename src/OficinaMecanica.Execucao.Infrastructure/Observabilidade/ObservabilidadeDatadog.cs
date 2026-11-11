using System.Diagnostics;
using Datadog.Trace;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.Observabilidade;

public sealed class ObservabilidadeDatadog(ILogger<ObservabilidadeDatadog> logger) : IObservabilidadeExecucao
{
    private readonly AsyncLocal<Dictionary<string, string>?> _contexto = new();

    public IDisposable ContinuarContexto(IReadOnlyDictionary<string, string>? contexto)
    {
        var anterior = _contexto.Value;
        _contexto.Value = ContextoTrace.Filtrar(contexto);
        return new Restaurador(() => _contexto.Value = anterior);
    }

    private sealed class Restaurador(Action restaurar) : IDisposable
    {
        public void Dispose() => restaurar();
    }

    public IEscopoObservabilidade Iniciar(string operacao, Mensagem mensagem)
    {
        var anterior = _contexto.Value;
        var contexto = ContextoTrace.Filtrar(mensagem.TraceContext);
        _contexto.Value = contexto;
        IScope? trace = null;
        var tipo = TipoSeguro(mensagem.Tipo);
        try
        {
            var parent = contexto is null ? null : new SpanContextExtractor().Extract(contexto,
                (carrier, key) => carrier.TryGetValue(key, out var value) ? [value] : Array.Empty<string>());
            trace = Tracer.Instance.StartActive(operacao, new SpanCreationSettings { Parent = parent });
            trace.Span.ResourceName = operacao + " " + tipo;
            trace.Span.Type = "queue";
            trace.Span.SetTag("message.type", tipo);
            trace.Span.SetTag("ordem_servico_id", mensagem.OrdemServicoId.ToString("D"));
            trace.Span.SetTag("saga_id", mensagem.SagaId.ToString("D"));
        }
        catch { /* Telemetria não altera o resultado da transação de negócio. */ }
        var correlation = mensagem.CorrelationId;
        var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["operation"] = operacao,
            ["bounded_context"] = "Execucao",
            ["message_type"] = tipo,
            ["mensagem_id"] = mensagem.MensagemId,
            ["ordem_servico_id"] = mensagem.OrdemServicoId,
            ["saga_id"] = mensagem.SagaId,
            ["x_correlation_id"] = !string.IsNullOrWhiteSpace(correlation) && correlation.Length <= 128
                && correlation.All(c => !char.IsControl(c)) ? correlation : null
        });
        return new Escopo(trace, logScope, logger, () => _contexto.Value = anterior);
    }

    public Dictionary<string, string>? CapturarContexto()
    {
        try
        {
            if (Tracer.Instance.ActiveScope is { } scope)
            {
                var carrier = new Dictionary<string, string>(StringComparer.Ordinal);
                new SpanContextInjector().Inject(carrier, (c, key, value) => c[key] = value, scope.Span.Context);
                if (ContextoTrace.Filtrar(carrier) is { } contexto) return contexto;
            }
        }
        catch { /* Sem profiler/contexto válido, preservar somente metadado validado. */ }
        return ContextoTrace.Filtrar(_contexto.Value)
            ?? ContextoTrace.Filtrar(new Dictionary<string, string> { ["traceparent"] = Activity.Current?.Id ?? "" });
    }

    private static string TipoSeguro(string tipo) => tipo is "PrepararExecucao" or "CancelarPreparacao" or "AutorizarExecucao"
        or "ExecucaoPreparada" or "PreparacaoRecusada" or "PreparacaoCancelada" or "ExecucaoIniciada" or "ExecucaoFinalizada"
        ? tipo : "desconhecido";

    private sealed class Escopo(IScope? trace, IDisposable? logScope, ILogger logger, Action restaurar) : IEscopoObservabilidade
    {
        private readonly long _inicio = Stopwatch.GetTimestamp();
        private string _resultado = "pendente";
        private bool _falha;
        public void Concluir(string resultado) => _resultado = TipoSeguro(resultado) != "desconhecido" ? resultado
            : resultado is "autorizada" or "publicada" or "cancelada" ? resultado : "concluida";
        public void Falhar()
        {
            _falha = true;
            _resultado = "falha";
            try { if (trace is not null) trace.Span.Error = true; } catch { }
        }
        public void Dispose()
        {
            var duracao = Stopwatch.GetElapsedTime(_inicio).TotalMilliseconds;
            if (_falha) logger.LogWarning("Operação de execução pendente de recuperação; resultado {outcome}, duração {duration_ms} ms.", _resultado, duracao);
            else logger.LogInformation("Operação de execução concluída; resultado {outcome}, duração {duration_ms} ms.", _resultado, duracao);
            try { trace?.Dispose(); } catch { }
            logScope?.Dispose();
            restaurar();
        }
    }
}
