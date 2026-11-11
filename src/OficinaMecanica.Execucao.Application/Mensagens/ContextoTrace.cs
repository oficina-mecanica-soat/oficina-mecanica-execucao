namespace OficinaMecanica.Execucao.Application.Mensagens;

public static class ContextoTrace
{
    public static Dictionary<string, string>? Filtrar(IReadOnlyDictionary<string, string>? contexto)
    {
        if (contexto is null || !contexto.TryGetValue("traceparent", out var valor) || valor is null || valor.Length != 55)
            return null;
        if (valor[..3] != "00-" || valor[35] != '-' || valor[52] != '-'
            || valor.AsSpan(3, 32).ContainsAnyExcept("0123456789abcdef".AsSpan())
            || valor.AsSpan(36, 16).ContainsAnyExcept("0123456789abcdef".AsSpan())
            || valor.AsSpan(53, 2).ContainsAnyExcept("0123456789abcdef".AsSpan())
            || valor.AsSpan(3, 32).SequenceEqual("00000000000000000000000000000000")
            || valor.AsSpan(36, 16).SequenceEqual("0000000000000000"))
            return null;
        return new(StringComparer.Ordinal) { ["traceparent"] = valor };
    }
}
