using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Filters;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.Responses;
using OficinaMecanica.Execucao.Application.Identidade;

namespace OficinaMecanica.Execucao.API.GestaoEstoque.Controllers;

[ApiController]
[Authorize(Roles = PerfisAcesso.AdministradorAtendente)]
[ServiceFilter(typeof(DisponibilidadeProcessamentoFilter))]
[Route("api/v1/execucoes/estoque")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
public sealed class EstoqueController : ControllerBase
{
    [HttpPost("registrar-entrada", Name = "RegistrarEntradaEstoque")]
    [ProducesResponseType(typeof(ItemEstoqueResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegistrarEntrada([FromServices] RegistrarEntradaEstoqueUseCase useCase,
        [FromBody] RegistrarEntradaEstoqueRequest request, CancellationToken cancellationToken)
    {
        var resultado = await useCase.ExecuteAsync(request, cancellationToken);
        return Ok(resultado);
    }
}
