using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using Amazon.DynamoDBv2;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class DynamoDbFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("amazon/dynamodb-local:3.1.0")
        .WithPortBinding(8000, true)
        .WithCommand("-jar", "DynamoDBLocal.jar", "-inMemory", "-sharedDb")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8000))
        .Build();
    public string Endpoint => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8000)}";
    public AmazonDynamoDBClient CriarClient() => DynamoDbLocal.CriarClient(Endpoint);
    public Task InitializeAsync() => _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}
