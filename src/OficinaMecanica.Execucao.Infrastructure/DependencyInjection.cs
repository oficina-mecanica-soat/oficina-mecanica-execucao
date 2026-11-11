using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Infrastructure.Hosting;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ValidarComposicaoUnica(services);
        services.Configure<ProcessamentoLocalOptions>(configuration.GetSection("ProcessamentoLocal"));
        services.AddSingleton(provider => DynamoDbLocal.CriarClient(provider.GetRequiredService<IOptions<ProcessamentoLocalOptions>>().Value.Endpoint));
        services.AddScoped<IArmazenamentoExecucao>(provider => new ArmazenamentoDynamoDb(provider.GetRequiredService<AmazonDynamoDBClient>(), provider.GetRequiredService<IOptions<ProcessamentoLocalOptions>>().Value.Tabela));
        services.AddScoped<IUnidadeTrabalhoPreparacao, UnidadeTrabalhoPreparacaoDynamoDb>();
        services.AddScoped<ITransporteResultado>(provider => new TransporteArquivos(Path.Combine(provider.GetRequiredService<IOptions<ProcessamentoLocalOptions>>().Value.Diretorio, "saida")));
        services.AddScoped(provider => new ConsumidorArquivos(provider.GetRequiredService<ProcessadorPreparacao>(), provider.GetRequiredService<IOptions<ProcessamentoLocalOptions>>().Value.Diretorio));
        services.AddScoped<PublicadorOutbox>();
        services.AddHostedService<ProcessamentoLocalWorker>();
        return services;
    }

    public static IServiceCollection AddInfrastructureRemota(this IServiceCollection services,
        ProcessamentoRemotoOptions options, IAmazonDynamoDB dynamoDb, IAmazonSQS sqs)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dynamoDb);
        ArgumentNullException.ThrowIfNull(sqs);
        options.Validar();
        ValidarComposicaoUnica(services);

        // O host fornece clientes cuja identidade foi definida e validada separadamente.
        // Instâncias fornecidas pelo chamador continuam sob sua responsabilidade de descarte.
        services.AddSingleton(dynamoDb);
        services.AddSingleton(sqs);
        services.AddScoped<IArmazenamentoExecucao>(_ => new ArmazenamentoDynamoDb(dynamoDb, options.Tabela));
        services.AddScoped<IUnidadeTrabalhoPreparacao, UnidadeTrabalhoPreparacaoDynamoDb>();
        services.AddScoped<ITransporteResultado>(_ => new TransporteSqs(sqs, options.FilaResultadosOsUrl));
        services.AddScoped(provider => new ConsumidorSqs(sqs, provider.GetRequiredService<ProcessadorPreparacao>(),
            options.FilaEntradaUrl, provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<PublicadorOutbox>();
        services.AddHostedService<ProcessamentoRemotoWorker>();
        return services;
    }

    private static void ValidarComposicaoUnica(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IArmazenamentoExecucao)))
            throw new InvalidOperationException("Escolha uma única composição de processamento: local ou remota.");
    }
}
