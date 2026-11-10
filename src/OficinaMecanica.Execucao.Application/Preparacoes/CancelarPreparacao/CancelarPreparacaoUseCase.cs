using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.Preparacoes;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;

public sealed class CancelarPreparacaoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho,
    IValidator<CancelarPreparacaoRequest> validator, TimeProvider relogio)
{
    public async Task<CancelarPreparacaoResponse> ExecuteAsync(CancelarPreparacaoRequest request, CancellationToken ct = default)
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
            var hash = ResultadosPreparacao.Hash(mensagem, dados);
            var anterior = await sessao.ObterResultadoAsync(mensagem, referencia.Versao, hash, ct);
            if (anterior is not null)
                return new CancelarPreparacaoResponse(anterior);

            var estado = await sessao.ObterPreparacaoAsync(mensagem.OrdemServicoId, referencia.Versao, ct)
                ?? throw new PreparacaoNaoEncontradaException();
            var atual = await sessao.ObterAtualAsync(mensagem.OrdemServicoId, ct);
            if (estado.PreparacaoId != dados.PreparacaoId || atual?.PreparacaoId != dados.PreparacaoId || estado.Cancelada)
                throw new ContratoMensagemException("ESTADO_PREPARACAO_INCONSISTENTE");

            if (estado.Reservada)
            {
                foreach (var item in estado.Snapshot.Itens)
                {
                    var reserva = await sessao.ObterReservaAsync(mensagem.OrdemServicoId, item.ItemId, referencia.Versao, ct);
                    if (reserva is null || reserva.PreparacaoId != dados.PreparacaoId || reserva.Liberada
                        || reserva.Quantidade != item.Quantidade || reserva.FilialId != estado.Snapshot.FilialId)
                        throw new DomainException("Reserva persistida inconsistente; compensação não confirmada.");
                    await sessao.SalvarReservaAsync(mensagem.OrdemServicoId, referencia.Versao, reserva with { Liberada = true }, false, ct);
                }
                var estoque = await sessao.Estoque.ObterAsync(estado.Snapshot.FilialId, estado.Snapshot.Itens.Select(item => item.ItemId).ToArray(), ct);
                var preparacao = PreparacaoEstoque.Reconstituir(dados.PreparacaoId, mensagem.OrdemServicoId,
                    estado.Snapshot.FilialId, estado.Snapshot.Itens.Select(item => new ItemReserva(item.ItemId, item.Quantidade)), false);
                preparacao.Cancelar(estoque);
                await sessao.Estoque.AtualizarAsync(estado.Snapshot.FilialId, estoque, ct);
            }
            await sessao.SalvarPreparacaoAsync(mensagem.OrdemServicoId, referencia.Versao, estado with { Cancelada = true }, ct);
            await sessao.SalvarAtualAsync(mensagem.OrdemServicoId, atual with { Ativa = false }, ct);
            var resultado = ResultadosPreparacao.Criar(mensagem, "PreparacaoCancelada", new PreparacaoCancelada(dados.PreparacaoId, referencia.Versao), relogio);
            await sessao.RegistrarResultadoAsync(mensagem, referencia.Versao, hash, resultado, ct);
            return new CancelarPreparacaoResponse(resultado);
        }, ct);
    }
}
