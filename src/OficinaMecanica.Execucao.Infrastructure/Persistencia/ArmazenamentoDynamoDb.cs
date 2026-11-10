using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public sealed class ArmazenamentoDynamoDb(IAmazonDynamoDB client, string tabela) : IArmazenamentoExecucao
{
    public async Task<Registro> ObterAsync(ChaveRegistro chave, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = tabela,
            Key = Chave(chave),
            ConsistentRead = true
        }, cancellationToken);
        return Ler(chave, response.Item);
    }

    public async Task ConfirmarAsync(IReadOnlyCollection<AlteracaoRegistro> registros, CancellationToken cancellationToken)
    {
        if (registros.Count is 0 or > 100 || registros.Select(item => item.Original.Chave).Distinct().Count() != registros.Count)
            throw new ContratoMensagemException("LIMITE_TRANSACAO_DYNAMODB");
        var itens = new List<TransactWriteItem>();
        foreach (var alteracao in registros)
        {
            var original = alteracao.Original;
            var condition = original.Revisao == 0 ? "attribute_not_exists(PK)" : "revisao = :esperada";
            Dictionary<string, AttributeValue>? valores = original.Revisao == 0 ? null : new()
            {
                [":esperada"] = new AttributeValue { N = original.Revisao.ToString(CultureInfo.InvariantCulture) }
            };
            if (!alteracao.Alterado)
                itens.Add(new() { ConditionCheck = new() { TableName = tabela, Key = Chave(original.Chave), ConditionExpression = condition, ExpressionAttributeValues = valores } });
            else if (alteracao.Json is null)
                itens.Add(new() { Delete = new() { TableName = tabela, Key = Chave(original.Chave), ConditionExpression = condition, ExpressionAttributeValues = valores } });
            else
            {
                var documento = Chave(original.Chave);
                documento["revisao"] = new() { N = (original.Revisao + 1).ToString(CultureInfo.InvariantCulture) };
                documento["conteudo"] = new() { S = alteracao.Json };
                itens.Add(new() { Put = new() { TableName = tabela, Item = documento, ConditionExpression = condition, ExpressionAttributeValues = valores } });
            }
        }
        try
        {
            await client.TransactWriteItemsAsync(new() { TransactItems = itens }, cancellationToken);
        }
        catch (TransactionCanceledException error) when (error.CancellationReasons?.Any(
            reason => reason.Code is "ConditionalCheckFailed" or "TransactionConflict") == true)
        {
            throw new ConflitoConcorrenciaException();
        }
    }

    public async Task<Registro?> ObterPrimeiraOutboxAsync(CancellationToken cancellationToken)
    {
        var response = await client.QueryAsync(new QueryRequest
        {
            TableName = tabela,
            KeyConditionExpression = "PK = :particao",
            ExpressionAttributeValues = new() { [":particao"] = new() { S = "OUTBOX#PENDENTE" } },
            ConsistentRead = true,
            ScanIndexForward = true,
            Limit = 1
        }, cancellationToken);
        var item = response.Items?.FirstOrDefault();
        return item is null ? null : Ler(new(item["PK"].S, item["SK"].S), item);
    }

    private static Dictionary<string, AttributeValue> Chave(ChaveRegistro chave) => new()
    {
        ["PK"] = new() { S = chave.Particao },
        ["SK"] = new() { S = chave.Ordenacao }
    };

    private static Registro Ler(ChaveRegistro chave, Dictionary<string, AttributeValue>? item)
        => item is null || item.Count == 0 ? new(chave, 0, null)
            : new(chave, long.Parse(item["revisao"].N, CultureInfo.InvariantCulture), item["conteudo"].S);
}
