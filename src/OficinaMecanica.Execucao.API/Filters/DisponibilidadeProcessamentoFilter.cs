using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Infrastructure.Hosting;
using OficinaMecanica.Execucao.API.Extensions.Configuration;

namespace OficinaMecanica.Execucao.API.Filters;

public sealed class DisponibilidadeProcessamentoFilter(IHostEnvironment ambiente,
    IOptions<ProcessamentoLocalOptions> processamento, ProcessamentoConfigurado configurado) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // Remoto só é registrado após validar configuração e identidade STS no startup.
        if (!configurado.Remoto && (!ambiente.IsDevelopment() || !processamento.Value.Habilitado))
        {
            context.Result = new ObjectResult(new ErrorResponse("Processamento de execução indisponível.", TipoErro.ErroInterno))
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }
        await next();
    }
}
