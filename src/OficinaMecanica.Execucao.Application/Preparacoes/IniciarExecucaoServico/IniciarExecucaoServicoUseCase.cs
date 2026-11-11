using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;

public sealed class IniciarExecucaoServicoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho,
    IValidator<IniciarExecucaoServicoRequest> validator, TimeProvider relogio)
{
    public async Task<ExecucaoResponse> ExecuteAsync(IniciarExecucaoServicoRequest request, CancellationToken ct = default)
    {
        var validacao = await validator.ValidateAsync(request, ct);
        if (!validacao.IsValid) throw new ContratoMensagemException(validacao.Errors[0].ErrorMessage);
        return await unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var (referencia, estado, execucao) = await OperacoesExecucao.CarregarAsync(sessao, request.OrdemServicoId, request.PreparacaoId, ct);
            await sessao.RegistrarRecebimentoAsync(request.OperacaoId, OperacoesExecucao.HashAcao(nameof(IniciarExecucaoServicoUseCase), request), ct);
            var iniciada = execucao.IniciadaEm is not null;
            if (execucao.IniciarServico(request.ServicoId, relogio.GetUtcNow().UtcDateTime))
            {
                await sessao.SalvarPreparacaoAsync(request.OrdemServicoId, referencia.Versao,
                    estado with { Execucao = CicloExecucaoPersistido.De(execucao) }, ct);
                if (!iniciada)
                    await sessao.AdicionarResultadoAsync(OperacoesExecucao.Evento(estado, request.OperacaoId, "ExecucaoIniciada",
                        new ExecucaoIniciada(estado.PreparacaoId, estado.Autorizacao!.VersaoOrcamento), relogio), ct);
            }
            return OperacoesExecucao.Resposta(estado, execucao);
        }, ct);
    }
}
