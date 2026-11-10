using System.Text.Json;
using Amazon.DynamoDBv2;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

internal sealed class CenarioPreparacao : IDisposable
{
    private readonly AmazonDynamoDBClient _client;
    public string Tabela { get; } = "teste-" + Guid.NewGuid().ToString("N");
    public ArmazenamentoDynamoDb Armazenamento { get; }
    public ProcessadorPreparacao Processador { get; }
    public RelogioControlado Relogio { get; } = new();
    public Guid OsId { get; } = Guid.NewGuid();
    public Guid SagaId { get; } = Guid.NewGuid();
    public Guid FilialId { get; } = Guid.NewGuid();
    public Guid ItemId { get; } = Guid.NewGuid();
    public Guid SegundoItemId { get; } = Guid.NewGuid();

    private CenarioPreparacao(AmazonDynamoDBClient client)
    {
        _client = client;
        Armazenamento = new(client, Tabela);
        Processador = new(Armazenamento, Relogio);
    }

    public static async Task<CenarioPreparacao> CriarAsync(DynamoDbFixture fixture)
    {
        var cenario = new CenarioPreparacao(fixture.CriarClient());
        await DynamoDbLocal.PrepararTabelaAsync(cenario._client, cenario.Tabela, CancellationToken.None);
        return cenario;
    }

    public async Task SemearAsync(int quantidade = 10, Guid? itemId = null, Guid? filialId = null)
    {
        var item = ItemEstoque.Criar(itemId ?? ItemId, quantidade);
        var tx = new TransacaoExecucao(Armazenamento);
        await tx.AdicionarAsync(ChavesExecucao.Estoque(filialId ?? FilialId, item.PecaInsumoCatalogoId), SaldoEstoque.De(item), default);
        await tx.ConfirmarAsync(default);
    }

    public PrepararExecucao Dados(int quantidade = 3, int versao = 1) => new(versao, FilialId, Guid.NewGuid(), Guid.NewGuid(),
        [new(ItemId, quantidade)], [new(Guid.NewGuid(), "Troca de óleo", 125.50m)]);

    public Mensagem Comando<T>(string tipo, T dados, Guid? osId = null) => new(Guid.NewGuid(), tipo, 1,
        SagaId, osId ?? OsId, Guid.NewGuid().ToString("D"), Guid.NewGuid(), Relogio.GetUtcNow(), JsonSerializer.SerializeToElement(dados, JsonContrato.Options));

    public Task<Mensagem> EnviarAsync(Mensagem mensagem, ProcessadorPreparacao? processador = null)
        => (processador ?? Processador).ProcessarAsync(JsonContrato.Serializar(mensagem));

    public async Task<SaldoEstoque?> SaldoAsync(Guid? itemId = null, Guid? filialId = null)
        => (await Armazenamento.ObterAsync(ChavesExecucao.Estoque(filialId ?? FilialId, itemId ?? ItemId), default)).Ler<SaldoEstoque>();

    public async Task<Mensagem> CancelarAsync(Mensagem resultado)
    {
        var preparada = resultado.Dados.Deserialize<ExecucaoPreparada>(JsonContrato.Options)!;
        return await EnviarAsync(Comando("CancelarPreparacao", new CancelarPreparacao(preparada.PreparacaoId, "Orçamento recusado")));
    }

    public void Dispose() => _client.Dispose();
}

internal sealed class RelogioControlado : TimeProvider
{
    private DateTimeOffset _agora = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _agora;
    public void Avancar(TimeSpan intervalo) => _agora += intervalo;
}
