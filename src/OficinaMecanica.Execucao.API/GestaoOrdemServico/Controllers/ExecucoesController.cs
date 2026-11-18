using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Filters;
using OficinaMecanica.Execucao.API.GestaoOrdemServico.Requests;
using OficinaMecanica.Execucao.API.GestaoOrdemServico.Responses;
using OficinaMecanica.Execucao.Application.Identidade;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Application.Preparacoes.ObterExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

namespace OficinaMecanica.Execucao.API.GestaoOrdemServico.Controllers;

[ApiController]
[Route("api/v1/execucoes/{ordemServicoId:guid}/preparacoes/{preparacaoId:guid}")]
[Authorize(Roles = PerfisAcesso.AdministradorMecanico)]
[ServiceFilter(typeof(DisponibilidadeProcessamentoFilter))]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
public sealed class ExecucoesController : ControllerBase
{
    [HttpGet(Name = "ObterExecucao")]
    [ProducesResponseType(typeof(ExecucaoResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obter([FromServices] ObterExecucaoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, CancellationToken cancellationToken)
        => Ok(await useCase.ExecuteAsync(new(ordemServicoId, preparacaoId), cancellationToken));

    [HttpPost("servicos/{servicoId:guid}/iniciar-execucao", Name = "IniciarExecucaoServico")]
    [ProducesResponseType(typeof(OperacaoExecucaoAceitaResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> IniciarExecucaoServico([FromServices] IniciarExecucaoServicoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, Guid servicoId, [FromBody] AcaoExecucaoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(new(request.OperacaoId, ordemServicoId, preparacaoId, servicoId), cancellationToken);
        return Accepted(Url.Action(nameof(Obter), new { ordemServicoId, preparacaoId }), new OperacaoExecucaoAceitaResponse(request.OperacaoId, resultado));
    }

    [HttpPost("servicos/{servicoId:guid}/finalizar", Name = "FinalizarServico")]
    [ProducesResponseType(typeof(OperacaoExecucaoAceitaResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> FinalizarServico([FromServices] FinalizarServicoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, Guid servicoId, [FromBody] AcaoExecucaoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(new(request.OperacaoId, ordemServicoId, preparacaoId, servicoId), cancellationToken);
        return Accepted(Url.Action(nameof(Obter), new { ordemServicoId, preparacaoId }), new OperacaoExecucaoAceitaResponse(request.OperacaoId, resultado));
    }

    [HttpPost("finalizar", Name = "FinalizarExecucao")]
    [ProducesResponseType(typeof(OperacaoExecucaoAceitaResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Finalizar([FromServices] FinalizarExecucaoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, [FromBody] AcaoExecucaoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(new(request.OperacaoId, ordemServicoId, preparacaoId), cancellationToken);
        return Accepted(Url.Action(nameof(Obter), new { ordemServicoId, preparacaoId }), new OperacaoExecucaoAceitaResponse(request.OperacaoId, resultado));
    }
}
