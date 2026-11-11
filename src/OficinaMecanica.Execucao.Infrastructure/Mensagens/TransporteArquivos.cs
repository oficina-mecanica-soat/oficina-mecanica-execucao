using System.Text;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.Mensagens;

/// <summary>Substituto local de transporte. Não emula garantias SQS.</summary>
public sealed class TransporteArquivos(string diretorio) : ITransporteResultado
{
    public async Task PublicarAsync(Mensagem mensagem, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(diretorio);
        var destino = Path.Combine(diretorio, $"{mensagem.MensagemId:D}.json");
        var conteudo = JsonContrato.Serializar(mensagem with { TraceContext = ContextoTrace.Filtrar(mensagem.TraceContext) });
        if (File.Exists(destino))
        {
            if (await File.ReadAllTextAsync(destino, cancellationToken) != conteudo)
                throw new IOException("Mensagem local existente com conteúdo divergente.");
            return;
        }
        var temporario = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporario, conteudo, Encoding.UTF8, cancellationToken);
            try { File.Move(temporario, destino, false); }
            catch (IOException) when (File.Exists(destino))
            {
                if (await File.ReadAllTextAsync(destino, cancellationToken) != conteudo)
                    throw;
            }
        }
        finally
        {
            File.Delete(temporario);
        }
    }
}
