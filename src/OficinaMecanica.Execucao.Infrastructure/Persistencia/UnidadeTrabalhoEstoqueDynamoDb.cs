using OficinaMecanica.Execucao.Application.GestaoEstoque;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Infrastructure.Persistencia;

public sealed class UnidadeTrabalhoEstoqueDynamoDb(IArmazenamentoExecucao armazenamento) : IUnidadeTrabalhoEstoque
{
    public async Task<T> ExecutarAsync<T>(Func<ISessaoEstoque, Task<T>> operacao, CancellationToken cancellationToken)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            var transacao = new TransacaoExecucao(armazenamento);
            try
            {
                T resultado;
                try
                {
                    resultado = await operacao(new SessaoEstoque(transacao));
                }
                catch (Exception erro) when (erro is DomainException or ContratoMensagemException)
                {
                    await transacao.ValidarLeiturasAsync(cancellationToken);
                    throw;
                }

                await transacao.ConfirmarAsync(cancellationToken);
                return resultado;
            }
            catch (ConflitoConcorrenciaException) when (tentativa < 7)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5 * (tentativa + 1)), cancellationToken);
            }
        }
    }
}

internal sealed class SessaoEstoque(TransacaoExecucao transacao) : ISessaoEstoque
{
    public IEstoqueRepository Estoque { get; } = new EstoqueTransacionalRepository(transacao);

    private static ChaveRegistro ChaveEntrada(Guid operacaoId) => new($"ENTRADA_ESTOQUE#{operacaoId:D}", "REGISTRADA");

    public Task<EntradaEstoqueRegistrada?> ObterEntradaAsync(Guid operacaoId, CancellationToken cancellationToken)
        => transacao.LerAsync<EntradaEstoqueRegistrada>(ChaveEntrada(operacaoId), cancellationToken);

    public Task RegistrarEntradaAsync(EntradaEstoqueRegistrada entrada, CancellationToken cancellationToken)
        => transacao.AdicionarAsync(ChaveEntrada(entrada.Request.OperacaoId), entrada, cancellationToken);
}
