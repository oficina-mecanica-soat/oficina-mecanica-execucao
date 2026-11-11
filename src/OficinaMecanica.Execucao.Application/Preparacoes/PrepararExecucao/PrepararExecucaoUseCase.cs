using FluentValidation;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Messages;
using OficinaMecanica.Execucao.Domain.Preparacoes;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;

public sealed class PrepararExecucaoUseCase(IUnidadeTrabalhoPreparacao unidadeTrabalho,
    IValidator<PrepararExecucaoRequest> validator, TimeProvider relogio, IObservabilidadeExecucao? observabilidade = null)
{
    public async Task<PrepararExecucaoResponse> ExecuteAsync(PrepararExecucaoRequest request, CancellationToken ct = default)
    {
        var validacao = await validator.ValidateAsync(request, ct);
        if (!validacao.IsValid) throw new ContratoMensagemException(validacao.Errors[0].ErrorMessage);
        var mensagem = request.Origem;
        var dados = ResultadosPreparacao.Normalizar(request.Dados);
        return await unidadeTrabalho.ExecutarAsync(async sessao =>
        {
            var hash = ResultadosPreparacao.Hash(mensagem, dados);
            var anterior = await sessao.ObterResultadoAsync(mensagem, dados.Versao, hash, ct);
            if (anterior is not null)
                return new PrepararExecucaoResponse(anterior);

            var atual = await sessao.ObterAtualAsync(mensagem.OrdemServicoId, ct);
            if ((atual is null && dados.Versao != 1) || (atual is not null && dados.Versao <= atual.Versao))
                throw new ContratoMensagemException("VERSAO_PREPARACAO_INVALIDA");
            if (atual?.Ativa == true)
                throw new ContratoMensagemException("PREPARACAO_ANTERIOR_ATIVA");

            var estoque = await sessao.Estoque.ObterAsync(dados.FilialId, dados.Itens.Select(item => item.ItemId).ToArray(), ct);
            var preparacaoId = Guid.NewGuid();
            var reservada = true;
            Mensagem resultado;
            try
            {
                PreparacaoEstoque.Reservar(preparacaoId, mensagem.OrdemServicoId, dados.FilialId,
                    dados.Itens.Select(item => new ItemReserva(item.ItemId, item.Quantidade)), estoque);
                resultado = ResultadosPreparacao.Criar(mensagem, "ExecucaoPreparada", new ExecucaoPreparada(preparacaoId,
                    dados.Versao, dados.Itens, new(dados.VeiculoId, dados.MecanicoId, dados.Servicos)), relogio, observabilidade);
            }
            catch (DomainException error) when (error.Message == EstoqueErrorMessages.EstoqueInsuficiente)
            {
                reservada = false;
                resultado = ResultadosPreparacao.Criar(mensagem, "PreparacaoRecusada", new PreparacaoRecusada(dados.Versao, "ESTOQUE_INSUFICIENTE"), relogio, observabilidade);
            }

            if (reservada)
            {
                await sessao.Estoque.AtualizarAsync(dados.FilialId, estoque, ct);
                foreach (var item in dados.Itens)
                    await sessao.SalvarReservaAsync(mensagem.OrdemServicoId, dados.Versao,
                        new ReservaPersistida(preparacaoId, dados.FilialId, item.ItemId, item.Quantidade, false), true, ct);
            }
            await sessao.SalvarAtualAsync(mensagem.OrdemServicoId, new PreparacaoAtual(dados.Versao, preparacaoId, reservada), ct);
            await sessao.AdicionarPreparacaoAsync(mensagem.OrdemServicoId, dados.Versao,
                new EstadoPreparacao(preparacaoId, dados, reservada, false), ct);
            await sessao.AdicionarReferenciaAsync(preparacaoId, new ReferenciaPreparacao(mensagem.OrdemServicoId, dados.Versao), ct);
            await sessao.RegistrarResultadoAsync(mensagem, dados.Versao, hash, resultado, ct);
            return new PrepararExecucaoResponse(resultado);
        }, ct);
    }
}
