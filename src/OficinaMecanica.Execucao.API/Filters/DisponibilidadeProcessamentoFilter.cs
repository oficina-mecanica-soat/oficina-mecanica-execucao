using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Infrastructure.Hosting;

namespace OficinaMecanica.Execucao.API.Filters;

public sealed class DisponibilidadeProcessamentoFilter(IHostEnvironment ambiente,
    IOptions<ProcessamentoLocalOptions> processamento) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // O host atual compõe somente DynamoDB Local/arquivos. Não resolver persistência
        // por fallback quando a composição remota ainda não foi integrada e validada.
        if (!ambiente.IsDevelopment() || !processamento.Value.Habilitado)
        {
            context.Result = new ObjectResult(new ErrorResponse("Processamento de execução indisponível.", TipoErro.ErroInterno))
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }
        await next();
    }
}
