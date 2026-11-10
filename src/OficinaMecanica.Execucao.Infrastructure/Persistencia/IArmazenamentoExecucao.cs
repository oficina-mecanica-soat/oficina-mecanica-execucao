using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public sealed record ChaveRegistro(string Particao, string Ordenacao);
public sealed record Registro(ChaveRegistro Chave, long Revisao, string? Json)
{
    public T? Ler<T>() where T : class => Json is null ? null : JsonContrato.Ler<T>(Json);
}
public sealed record AlteracaoRegistro(Registro Original, bool Alterado, string? Json);

public interface IArmazenamentoExecucao
{
    Task<Registro> ObterAsync(ChaveRegistro chave, CancellationToken cancellationToken);
    Task ConfirmarAsync(IReadOnlyCollection<AlteracaoRegistro> registros, CancellationToken cancellationToken);
    Task<Registro?> ObterPrimeiraOutboxAsync(CancellationToken cancellationToken);
}

public sealed class TransacaoExecucao(IArmazenamentoExecucao armazenamento)
{
    private readonly Dictionary<ChaveRegistro, AlteracaoRegistro> _registros = [];

    public async Task<T?> LerAsync<T>(ChaveRegistro chave, CancellationToken cancellationToken) where T : class
    {
        if (!_registros.TryGetValue(chave, out var registro))
        {
            var original = await armazenamento.ObterAsync(chave, cancellationToken);
            registro = new(original, false, original.Json);
            _registros.Add(chave, registro);
        }
        return registro.Json is null ? null : JsonContrato.Ler<T>(registro.Json);
    }

    public void Salvar<T>(ChaveRegistro chave, T valor)
    {
        var registro = _registros[chave];
        _registros[chave] = registro with { Alterado = true, Json = JsonContrato.Serializar(valor) };
    }

    public async Task AdicionarAsync<T>(ChaveRegistro chave, T valor, CancellationToken cancellationToken)
    {
        if (await LerAsync<object>(chave, cancellationToken) is not null)
            throw new ConflitoConcorrenciaException();
        Salvar(chave, valor);
    }

    public Task ConfirmarAsync(CancellationToken cancellationToken)
        => armazenamento.ConfirmarAsync(_registros.Values.ToArray(), cancellationToken);
}
