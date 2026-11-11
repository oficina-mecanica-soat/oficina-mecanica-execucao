using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

public sealed class FinalizarExecucaoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho,
    IValidator<FinalizarExecucaoRequest> validator, TimeProvider relogio, IObservabilidadeExecucao? observabilidade = null)
{
    public async Task<ExecucaoResponse> ExecuteAsync(FinalizarExecucaoRequest request, CancellationToken ct = default)
    {
        var validacao = await validator.ValidateAsync(request, ct);
        if (!validacao.IsValid) throw new ContratoMensagemException(validacao.Errors[0].ErrorMessage);
        return await unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var (referencia, estado, execucao) = await OperacoesExecucao.CarregarAsync(sessao, request.OrdemServicoId, request.PreparacaoId, ct);
            await sessao.RegistrarRecebimentoAsync(request.OperacaoId, OperacoesExecucao.HashAcao(nameof(FinalizarExecucaoUseCase), request), ct);
            if (!execucao.Finalizar(relogio.GetUtcNow().UtcDateTime)) return OperacoesExecucao.Resposta(estado, execucao);
            foreach (var item in estado.Snapshot.Itens)
            {
                var reserva = await sessao.ObterReservaAsync(request.OrdemServicoId, item.ItemId, referencia.Versao, ct);
                if (reserva is null || reserva.PreparacaoId != request.PreparacaoId || reserva.Liberada || reserva.Baixada
                    || reserva.FilialId != estado.Snapshot.FilialId || reserva.Quantidade != item.Quantidade)
                    throw new DomainException("Reserva persistida inconsistente; baixa não confirmada.");
                await sessao.SalvarReservaAsync(request.OrdemServicoId, referencia.Versao, reserva with { Baixada = true }, false, ct);
            }
            var estoque = await sessao.Estoque.ObterAsync(estado.Snapshot.FilialId, estado.Snapshot.Itens.Select(item => item.ItemId).ToArray(), ct);
            foreach (var item in estado.Snapshot.Itens) estoque.BaixarItens(item.ItemId, item.Quantidade);
            await sessao.Estoque.AtualizarAsync(estado.Snapshot.FilialId, estoque, ct);
            await sessao.SalvarPreparacaoAsync(request.OrdemServicoId, referencia.Versao,
                estado with { Execucao = CicloExecucaoPersistido.De(execucao) }, ct);
            await sessao.AdicionarResultadoAsync(OperacoesExecucao.Evento(estado, request.OperacaoId, "ExecucaoFinalizada",
                new ExecucaoFinalizada(estado.PreparacaoId, estado.Autorizacao!.VersaoOrcamento,
                    new DateTimeOffset(execucao.FinalizadaEm!.Value)), relogio, observabilidade), ct);
            return OperacoesExecucao.Resposta(estado, execucao);
        }, ct);
    }
}
