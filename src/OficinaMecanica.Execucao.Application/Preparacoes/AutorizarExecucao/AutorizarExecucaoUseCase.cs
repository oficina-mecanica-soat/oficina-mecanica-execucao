using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;

namespace OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;

public sealed class AutorizarExecucaoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho,
    IValidator<AutorizarExecucaoRequest> validator)
{
    public async Task<AutorizarExecucaoResponse> ExecuteAsync(AutorizarExecucaoRequest request, CancellationToken ct = default)
    {
        var validacao = await validator.ValidateAsync(request, ct);
        if (!validacao.IsValid) throw new ContratoMensagemException(validacao.Errors[0].ErrorMessage);
        var mensagem = request.Origem;
        var dados = request.Dados;
        return await unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var referencia = await sessao.ObterReferenciaAsync(dados.PreparacaoId, ct)
                ?? throw new PreparacaoNaoEncontradaException();
            if (referencia.OrdemServicoId != mensagem.OrdemServicoId)
                throw new ContratoMensagemException("PREPARACAO_DE_OUTRA_OS");
            var estado = await sessao.ObterPreparacaoAsync(mensagem.OrdemServicoId, referencia.Versao, ct)
                ?? throw new PreparacaoNaoEncontradaException();
            if (estado.PreparacaoId != dados.PreparacaoId)
                throw new ContratoMensagemException("ESTADO_PREPARACAO_INCONSISTENTE");
            var hash = ResultadosPreparacao.Hash(mensagem, dados);
            await sessao.RegistrarRecebimentoAsync(mensagem.MensagemId, hash, ct);

            if (estado.Autorizacao is not null)
            {
                if (estado.Autorizacao.Hash != hash || estado.Autorizacao.Origem.SagaId != mensagem.SagaId)
                    throw new ContratoMensagemException("AUTORIZACAO_DIVERGENTE");
                // Reentrega após cancelamento não reativa a preparação nem publica início fictício.
                return new AutorizarExecucaoResponse(dados.PreparacaoId, estado.Autorizacao.VersaoOrcamento);
            }

            var atual = await sessao.ObterAtualAsync(mensagem.OrdemServicoId, ct);
            if (estado.Cancelada || !estado.Reservada || atual?.PreparacaoId != dados.PreparacaoId || !atual.Ativa)
                throw new ContratoMensagemException("PREPARACAO_NAO_AUTORIZAVEL");
            if (estado.Snapshot.Servicos.Length == 0)
                throw new ContratoMensagemException("SERVICO_OBRIGATORIO_PARA_EXECUCAO");

            var ciclo = new CicloExecucaoPersistido(estado.Snapshot.Servicos.Select(servico => new ServicoPersistido(
                servico.ServicoId, servico.ServicoCatalogoId, servico.Valor,
                Domain.GestaoOrdemServico.Enums.StatusServico.Pendente, null, null)).ToArray(), null);
            _ = ciclo.Reconstituir();
            await sessao.SalvarPreparacaoAsync(mensagem.OrdemServicoId, referencia.Versao,
                estado with { Autorizacao = new AutorizacaoPersistida(dados.VersaoOrcamento, mensagem, hash), Execucao = ciclo }, ct);
            return new AutorizarExecucaoResponse(dados.PreparacaoId, dados.VersaoOrcamento);
        }, ct);
    }
}
