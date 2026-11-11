using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Filters;
using OficinaMecanica.Execucao.API.GestaoOrdemServico.Requests;
using OficinaMecanica.Execucao.API.GestaoOrdemServico.Responses;
using OficinaMecanica.Execucao.Application.Identidade;
using OficinaMecanica.Execucao.Application.Preparacoes.ObterExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

namespace OficinaMecanica.Execucao.API.GestaoOrdemServico.Controllers;

[ApiController]
[Route("api/v1/execucoes/{ordemServicoId:guid}/preparacoes/{preparacaoId:guid}")]
[Authorize(Roles = PerfisAcesso.AdministradorMecanico)]
[ServiceFilter(typeof(DisponibilidadeProcessamentoFilter))]
public sealed class ExecucoesController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obter([FromServices] ObterExecucaoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, CancellationToken cancellationToken)
        => Ok(await useCase.ExecuteAsync(new(ordemServicoId, preparacaoId), cancellationToken));

    [HttpPost("servicos/{servicoId:guid}/iniciar-execucao")]
    public async Task<IActionResult> IniciarExecucaoServico([FromServices] IniciarExecucaoServicoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, Guid servicoId, [FromBody] AcaoExecucaoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(new(request.OperacaoId, ordemServicoId, preparacaoId, servicoId), cancellationToken);
        return AcceptedAtAction(nameof(Obter), new { ordemServicoId, preparacaoId }, new OperacaoExecucaoAceitaResponse(request.OperacaoId, resultado));
    }

    [HttpPost("servicos/{servicoId:guid}/finalizar")]
    public async Task<IActionResult> FinalizarServico([FromServices] FinalizarServicoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, Guid servicoId, [FromBody] AcaoExecucaoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(new(request.OperacaoId, ordemServicoId, preparacaoId, servicoId), cancellationToken);
        return AcceptedAtAction(nameof(Obter), new { ordemServicoId, preparacaoId }, new OperacaoExecucaoAceitaResponse(request.OperacaoId, resultado));
    }

    [HttpPost("finalizar")]
    public async Task<IActionResult> Finalizar([FromServices] FinalizarExecucaoUseCase useCase,
        Guid ordemServicoId, Guid preparacaoId, [FromBody] AcaoExecucaoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(new(request.OperacaoId, ordemServicoId, preparacaoId), cancellationToken);
        return AcceptedAtAction(nameof(Obter), new { ordemServicoId, preparacaoId }, new OperacaoExecucaoAceitaResponse(request.OperacaoId, resultado));
    }
}
