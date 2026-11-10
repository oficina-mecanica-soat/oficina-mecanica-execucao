namespace OficinaMecanica.Execucao.Application.Mensagens;

public interface ITransporteResultado
{
    Task PublicarAsync(Mensagem mensagem, CancellationToken cancellationToken);
}

