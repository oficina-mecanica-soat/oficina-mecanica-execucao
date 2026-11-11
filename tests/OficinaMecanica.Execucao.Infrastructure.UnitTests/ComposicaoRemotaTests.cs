using Amazon.DynamoDBv2;
using Amazon.SQS;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Infrastructure.Hosting;

namespace OficinaMecanica.Execucao.Infrastructure.UnitTests;

public sealed class ComposicaoRemotaTests
{
    private static ProcessamentoRemotoOptions Configuracao => new("tabela-teste",
        "https://sqs.us-east-1.amazonaws.com/123456789012/entrada-teste.fifo",
        "https://sqs.us-east-1.amazonaws.com/123456789012/resultados-teste.fifo");

    [Fact]
    public void Dado_HostAtual_Quando_ComporInfrastructure_Entao_NaoRegistraWorkerNemClientesRemotos()
    {
        var services = new ServiceCollection().AddApplication()
            .AddInfrastructure(new ConfigurationBuilder().Build());
        services.Where(d => d.ServiceType == typeof(IHostedService)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(ProcessamentoLocalWorker));
        services.Should().NotContain(d => d.ServiceType == typeof(IAmazonDynamoDB) || d.ServiceType == typeof(IAmazonSQS));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dado_ComposicaoExistente_Quando_MisturarLocalERemoto_Entao_Recusa(bool localPrimeiro)
    {
        var services = new ServiceCollection();
        var dynamo = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var config = new ConfigurationBuilder().Build();
        if (localPrimeiro) services.AddInfrastructure(config);
        else services.AddInfrastructureRemota(Configuracao, dynamo.Object, sqs.Object);

        Action registrar = () =>
        {
            if (localPrimeiro) services.AddInfrastructureRemota(Configuracao, dynamo.Object, sqs.Object);
            else services.AddInfrastructure(config);
        };
        registrar.Should().Throw<InvalidOperationException>();
        dynamo.VerifyNoOtherCalls();
        sqs.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "https://sqs.us-east-1.amazonaws.com/123456789012/entrada-teste.fifo", false)]
    [InlineData("tabela-teste", "http://localhost:9324/fila.fifo", false)]
    [InlineData("tabela-teste", "https://sqs.us-east-1.amazonaws.com/123456789012/entrada-teste.fifo", true)]
    public void Dado_ContratoInvalido_Quando_ComporRemoto_Entao_NaoRegistraServicosNemConsultaAws(string tabela, string entrada, bool mesmaFila)
    {
        var services = new ServiceCollection();
        var dynamo = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        Action registrar = () => services.AddInfrastructureRemota(new(tabela, entrada,
            mesmaFila ? entrada : Configuracao.FilaResultadosOsUrl), dynamo.Object, sqs.Object);

        registrar.Should().Throw<ArgumentException>();
        services.Should().BeEmpty();
        dynamo.VerifyNoOtherCalls();
        sqs.VerifyNoOtherCalls();
    }

    [Fact]
    public void Dado_ClientesAusentes_Quando_ComporRemoto_Entao_NaoEscolheCredenciaisImplicitamente()
    {
        var services = new ServiceCollection();
        Action semConfiguracao = () => services.AddInfrastructureRemota(null!, Mock.Of<IAmazonDynamoDB>(), Mock.Of<IAmazonSQS>());
        Action semDynamo = () => services.AddInfrastructureRemota(Configuracao, null!, Mock.Of<IAmazonSQS>());
        Action semSqs = () => services.AddInfrastructureRemota(Configuracao, Mock.Of<IAmazonDynamoDB>(), null!);

        semConfiguracao.Should().Throw<ArgumentNullException>();
        semDynamo.Should().Throw<ArgumentNullException>();
        semSqs.Should().Throw<ArgumentNullException>();
        services.Should().BeEmpty();
    }
}
