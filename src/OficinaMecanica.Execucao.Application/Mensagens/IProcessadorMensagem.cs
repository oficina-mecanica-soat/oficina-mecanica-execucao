namespace OficinaMecanica.Execucao.Application.Mensagens;

public interface IProcessadorMensagem
{
    Task<Mensagem> ProcessarAsync(string json, CancellationToken cancellationToken = default);
}
