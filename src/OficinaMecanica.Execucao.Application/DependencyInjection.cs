using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;
using OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

namespace OficinaMecanica.Execucao.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<PrepararExecucaoValidator>();
        services.AddScoped<PrepararExecucaoUseCase>();
        services.AddScoped<CancelarPreparacaoUseCase>();
        services.AddScoped<AutorizarExecucaoUseCase>();
        services.AddScoped<IniciarExecucaoServicoUseCase>();
        services.AddScoped<FinalizarServicoUseCase>();
        services.AddScoped<FinalizarExecucaoUseCase>();
        services.AddScoped<ProcessadorPreparacao>();
        return services;
    }
}
