using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SQS;

namespace OficinaMecanica.Execucao.Infrastructure.Hosting;

public sealed class ClientesAwsAcademicos : IDisposable
{
    private readonly InstanceProfileAWSCredentials _credenciais;
    public IAmazonDynamoDB DynamoDb { get; }
    public IAmazonSQS Sqs { get; }

    private ClientesAwsAcademicos(InstanceProfileAWSCredentials credenciais, RegionEndpoint regiao)
    {
        _credenciais = credenciais;
        var dynamoDb = new AmazonDynamoDBClient(credenciais, regiao);
        try
        {
            Sqs = new AmazonSQSClient(credenciais, regiao);
            DynamoDb = dynamoDb;
        }
        catch { dynamoDb.Dispose(); throw; }
    }

    public static async Task<ClientesAwsAcademicos> CriarAsync(RegionEndpoint regiao, string contaEsperada, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(regiao);
        IdentidadeAcademica.ValidarConta(contaEsperada);
        ct.ThrowIfCancellationRequested();
        // Fonte explícita: não usar a cadeia padrão que também aceita env/perfis do runner.
        var credenciais = new InstanceProfileAWSCredentials("LabRole");
        try
        {
            using var sts = new AmazonSecurityTokenServiceClient(credenciais, regiao);
            await IdentidadeAcademica.ValidarAsync(sts, contaEsperada, ct);
            ct.ThrowIfCancellationRequested();
            return new ClientesAwsAcademicos(credenciais, regiao);
        }
        catch { credenciais.Dispose(); throw; }
    }

    public void Dispose()
    {
        Sqs.Dispose();
        DynamoDb.Dispose();
        _credenciais.Dispose();
    }
}
