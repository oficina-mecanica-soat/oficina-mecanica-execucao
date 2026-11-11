using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;

public sealed class FinalizarServicoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho,
    IValidator<FinalizarServicoRequest> validator, TimeProvider relogio)
{
    public async Task<ExecucaoResponse> ExecuteAsync(FinalizarServicoRequest request, CancellationToken ct = default)
    {
        var validacao = await validator.ValidateAsync(request, ct);
        if (!validacao.IsValid) throw new ContratoMensagemException(validacao.Errors[0].ErrorMessage);
        return await unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var (referencia, estado, execucao) = await OperacoesExecucao.CarregarAsync(sessao, request.OrdemServicoId, request.PreparacaoId, ct);
            await sessao.RegistrarRecebimentoAsync(request.OperacaoId, OperacoesExecucao.HashAcao(nameof(FinalizarServicoUseCase), request), ct);
            if (execucao.FinalizarServico(request.ServicoId, relogio.GetUtcNow().UtcDateTime))
                await sessao.SalvarPreparacaoAsync(request.OrdemServicoId, referencia.Versao,
                    estado with { Execucao = CicloExecucaoPersistido.De(execucao) }, ct);
            return OperacoesExecucao.Resposta(estado, execucao);
        }, ct);
    }
}
