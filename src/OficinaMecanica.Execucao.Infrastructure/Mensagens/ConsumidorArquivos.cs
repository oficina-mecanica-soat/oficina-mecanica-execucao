using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Infrastructure.Mensagens;

public sealed class ConsumidorArquivos(ProcessadorPreparacao processador, string diretorio)
{
    public async Task<bool> ProcessarProximaAsync(CancellationToken ct = default)
    {
        var entrada = Path.Combine(diretorio, "entrada");
        Directory.CreateDirectory(entrada);
        var arquivo = Directory.EnumerateFiles(entrada, "*.json").Order(StringComparer.Ordinal).FirstOrDefault();
        if (arquivo is null)
            return false;
        FileStream stream;
        try { stream = new(arquivo, FileMode.Open, FileAccess.Read, FileShare.None); }
        catch (IOException) { return false; }
        string destino = "processadas";
        string? codigo = null;
        await using (stream)
        {
            try
            {
                if (stream.Length > 256 * 1024)
                    throw new ContratoMensagemException("MENSAGEM_LOCAL_EXCESSIVA");
                using var reader = new StreamReader(stream, leaveOpen: true);
                await processador.ProcessarAsync(await reader.ReadToEndAsync(ct), ct);
            }
            catch (Exception error) when (error is ContratoMensagemException or DomainException)
            {
                destino = "investigacao";
                codigo = error is ContratoMensagemException ? error.Message : "REGRA_DOMINIO_INVALIDA";
            }
        }
        // A confirmação local ocorre só após commit; falha de I/O deixa o arquivo
        // para reentrega, que será deduplicada pelo registro persistente.
        var pastaDestino = Path.Combine(diretorio, destino);
        Directory.CreateDirectory(pastaDestino);
        var caminhoDestino = Path.Combine(pastaDestino, Path.GetFileName(arquivo));
        if (File.Exists(caminhoDestino))
            caminhoDestino = Path.Combine(pastaDestino, Path.GetFileNameWithoutExtension(arquivo) + "." + Guid.NewGuid().ToString("N") + ".json");
        if (codigo is not null)
            await File.WriteAllTextAsync(caminhoDestino + ".motivo.txt", codigo, ct);
        File.Move(arquivo, caminhoDestino, false);
        return true;
    }
}
