using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public static class DynamoDbLocal
{
    public static AmazonDynamoDBClient CriarClient(string endpoint)
    {
        var uri = new Uri(endpoint);
        if (uri.Scheme != "http" || (!uri.IsLoopback && uri.Host != "dynamodb-local"))
            throw new ArgumentException("O modo local exige endpoint HTTP de loopback ou dynamodb-local.", nameof(endpoint));
        return new(new BasicAWSCredentials("local", "local"), new AmazonDynamoDBConfig { ServiceURL = endpoint });
    }

    public static async Task PrepararTabelaAsync(AmazonDynamoDBClient client, string tabela, CancellationToken ct)
    {
        // O cliente é criado exclusivamente por CriarClient, sem cadeia de credenciais AWS.
        _ = new Uri(client.Config.ServiceURL ?? throw new InvalidOperationException("Endpoint local obrigatório."));
        using var validacaoEndpoint = CriarClient(client.Config.ServiceURL);
        try { await client.DescribeTableAsync(tabela, ct); }
        catch (ResourceNotFoundException)
        {
            try
            {
                await client.CreateTableAsync(new()
                {
                    TableName = tabela,
                    BillingMode = BillingMode.PAY_PER_REQUEST,
                    AttributeDefinitions = [new("PK", ScalarAttributeType.S), new("SK", ScalarAttributeType.S)],
                    KeySchema = [new("PK", KeyType.HASH), new("SK", KeyType.RANGE)]
                }, ct);
            }
            catch (ResourceInUseException) { /* Outro processo local criou a mesma tabela. */ }
        }
    }
}
