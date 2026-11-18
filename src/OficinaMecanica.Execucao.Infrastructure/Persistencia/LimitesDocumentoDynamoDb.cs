using System.Text;
using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public static class LimitesDocumentoDynamoDb
{
    public const int MaximoBytes = 400 * 1024;

    // Números DynamoDB têm tamanho variável. Reservar os 19 bytes da representação
    // decimal do maior long é conservador para a revisão, inclusive em escritas futuras.
    public static long TamanhoConservador(ChaveRegistro chave, string json) =>
        (long)Encoding.UTF8.GetByteCount("PKSKrevisaoconteudo") + 19
        + Encoding.UTF8.GetByteCount(chave.Particao) + Encoding.UTF8.GetByteCount(chave.Ordenacao)
        + Encoding.UTF8.GetByteCount(json);

    public static void Validar(ChaveRegistro chave, string json)
    {
        if (TamanhoConservador(chave, json) > MaximoBytes)
            throw new SnapshotNaoPersistivelException();
    }

    public static void ValidarCrescimento(Guid osId, int versao, EstadoPreparacao estado, Mensagem origem)
    {
        var chave = ChavesExecucao.Preparacao(osId, versao);
        Validar(chave, JsonContrato.Serializar(estado));
        if (!estado.Reservada) return;

        // Projeta somente o documento, sem executar transições nem persistir IDs/datas
        // artificiais. Novos metadados de comandos futuros continuam guardados no commit.
        var autorizacao = origem with
        {
            Tipo = "AutorizarExecucao",
            Dados = JsonSerializer.SerializeToElement(new AutorizarExecucao(estado.PreparacaoId, int.MaxValue), JsonContrato.Options),
            TraceContext = ContextoTrace.Filtrar(origem.TraceContext)
        };
        var data = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var ciclo = new CicloExecucaoPersistido(estado.Snapshot.Servicos.Select(s => new ServicoPersistido(
            s.ServicoId, s.ServicoCatalogoId, s.Valor, StatusServico.Finalizado, data, data)).ToArray(), data);
        var projetado = estado with
        {
            Cancelada = false,
            Autorizacao = new AutorizacaoPersistida(int.MaxValue, autorizacao, new string('F', 64)),
            Execucao = ciclo
        };
        Validar(chave, JsonContrato.Serializar(projetado));
    }
}
