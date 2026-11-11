namespace OficinaMecanica.Execucao.Application.Mensagens;

public interface IObservabilidadeExecucao
{
    IEscopoObservabilidade Iniciar(string operacao, Mensagem mensagem);
    IDisposable ContinuarContexto(IReadOnlyDictionary<string, string>? contexto);
    Dictionary<string, string>? CapturarContexto();
}

public interface IEscopoObservabilidade : IDisposable
{
    void Concluir(string resultado);
    void Falhar();
}
