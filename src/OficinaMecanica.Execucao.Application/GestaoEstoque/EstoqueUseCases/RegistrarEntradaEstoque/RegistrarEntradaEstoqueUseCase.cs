using FluentValidation;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.Responses;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;

public sealed class RegistrarEntradaEstoqueUseCase
{
    private readonly IUnidadeTrabalhoEstoque _unidadeTrabalho;
    private readonly IValidator<RegistrarEntradaEstoqueRequest> _validator;

    public RegistrarEntradaEstoqueUseCase(IUnidadeTrabalhoEstoque unidadeTrabalho,
        IValidator<RegistrarEntradaEstoqueRequest> validator)
    {
        _unidadeTrabalho = unidadeTrabalho;
        _validator = validator;
    }

    public async Task<ItemEstoqueResponse> ExecuteAsync(RegistrarEntradaEstoqueRequest request,
        CancellationToken cancellationToken = default)
    {
        var validacao = await _validator.ValidateAsync(request, cancellationToken);
        if (!validacao.IsValid)
        {
            throw new ContratoMensagemException("ENTRADA_ESTOQUE_INVALIDA");
        }

        return await _unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var anterior = await sessao.ObterEntradaAsync(request.OperacaoId, cancellationToken);
            if (anterior is not null)
            {
                if (anterior.Request != request)
                {
                    throw new ContratoMensagemException("MENSAGEM_ID_REUTILIZADO");
                }

                return anterior.Resultado;
            }

            var estoque = await sessao.Estoque.ObterAsync(request.FilialId,
                [request.PecaInsumoCatalogoId], cancellationToken);
            var item = estoque.RegistrarEntrada(request.PecaInsumoCatalogoId, request.Quantidade);
            var resultado = new ItemEstoqueResponse(item.Id, request.FilialId,
                item.PecaInsumoCatalogoId, item.QuantidadeDisponivel, item.QuantidadeReservada);

            await sessao.Estoque.AtualizarAsync(request.FilialId, estoque, cancellationToken);
            await sessao.RegistrarEntradaAsync(new(request, resultado), cancellationToken);
            return resultado;
        }, cancellationToken);
    }
}
