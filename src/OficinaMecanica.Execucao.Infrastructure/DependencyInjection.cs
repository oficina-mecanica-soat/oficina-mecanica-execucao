using Amazon.DynamoDBv2;
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
}
