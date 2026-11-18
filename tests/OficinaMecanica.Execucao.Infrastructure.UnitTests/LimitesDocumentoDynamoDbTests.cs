using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Moq;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.UnitTests;

public sealed class LimitesDocumentoDynamoDbTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Dado_ItemNoLimite_Quando_Confirmar_Entao_ConsideraDocumentoCompleto(int diferenca)
    {
        var chave = new ChaveRegistro("OS#ação", "PREPARACAO#1");
        var vazio = JsonContrato.Serializar(new { texto = "" });
        var tamanho = checked((int)(LimitesDocumentoDynamoDb.MaximoBytes - LimitesDocumentoDynamoDb.TamanhoConservador(chave, vazio)));
        var json = JsonContrato.Serializar(new { texto = new string('x', tamanho + diferenca) });
        var client = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        client.Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), default)).ReturnsAsync(new TransactWriteItemsResponse());
        var storage = new ArmazenamentoDynamoDb(client.Object, "teste");
        var executar = () => storage.ConfirmarAsync([new(new(chave, 0, null), true, json)], default);

        if (diferenca > 0)
        {
            await Assert.ThrowsAsync<SnapshotNaoPersistivelException>(executar);
            client.Verify(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), default), Times.Never);
        }
        else
        {
            await executar();
            client.Verify(c => c.TransactWriteItemsAsync(It.Is<TransactWriteItemsRequest>(r => r.TransactItems.Count == 1
                && r.TransactItems[0].Put.Item["conteudo"].S == json), default), Times.Once);
        }
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_LoteComItemGrande_Quando_Confirmar_Entao_NaoEnviaNemOPrimeiroItemValido()
    {
        var client = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var storage = new ArmazenamentoDynamoDb(client.Object, "teste");

        await Assert.ThrowsAsync<SnapshotNaoPersistivelException>(() => storage.ConfirmarAsync([
            new(new(new("A", "1"), 0, null), true, "{}"),
            new(new(new("B", "1"), 0, null), true, new string('á', 210_000))], default));

        client.VerifyNoOtherCalls();
    }
}
