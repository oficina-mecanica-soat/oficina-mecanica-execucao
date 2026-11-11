using OficinaMecanica.Execucao.API.Extensions.Responses;
using OficinaMecanica.Execucao.API.Responses;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.API.Middlewares;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception error) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            var resposta = error switch
            {
                PreparacaoNaoEncontradaException => new ErrorResponse(ApiResponseMessages.RecursoNaoEncontrado, TipoErro.NaoEncontrado),
                ContratoMensagemException { Message: "PREPARACAO_DE_OUTRA_OS" } => new ErrorResponse(ApiResponseMessages.RecursoNaoEncontrado, TipoErro.NaoEncontrado),
                ContratoMensagemException
                {
                    Message: "INICIO_SERVICO_INVALIDO" or "FINALIZACAO_SERVICO_INVALIDA"
                    or "FINALIZACAO_EXECUCAO_INVALIDA" or "CONSULTA_EXECUCAO_INVALIDA"
                } => new ErrorResponse(ApiResponseMessages.RequisicaoInvalida, TipoErro.Validacao),
                ContratoMensagemException { Message: "MENSAGEM_ID_REUTILIZADO" or "SNAPSHOT_DIVERGENTE" }
                    or ConflitoConcorrenciaException => new ErrorResponse(ApiResponseMessages.ConflitoPersistencia, TipoErro.Conflito),
                ContratoMensagemException => new ErrorResponse(ApiResponseMessages.RegraNegocioViolada, TipoErro.RegraNegocio),
                DomainException => new ErrorResponse(error.Message, TipoErro.RegraNegocio),
                _ => new ErrorResponse(ApiResponseMessages.ErroInternoInesperado, TipoErro.ErroInterno)
            };
            if (resposta.Tipo == TipoErro.ErroInterno)
                logger.LogError("Erro inesperado ao processar requisição HTTP de execução; detalhes técnicos omitidos.");
            context.Response.StatusCode = resposta.Tipo.ToHttpStatusCode();
            await context.Response.WriteApiErrorResponseAsJsonAsync(resposta);
        }
    }
}
