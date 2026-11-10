using System.Text.Json;
using FluentAssertions;
using Moq;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Aggregates;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Interfaces;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Application.UnitTests.Preparacoes;

public sealed class PreparacaoUseCasesTests
{
    [Fact]
    public async Task Dado_SnapshotValido_Quando_Preparar_Entao_ReservaERegistraResultadoNaMesmaUnidade()
    {
        var c = new Cenario();
        var resultado = await c.Preparar.ExecuteAsync(new(c.Mensagem, c.Dados));

        resultado.Resultado.Tipo.Should().Be("ExecucaoPreparada");
        resultado.Resultado.CausationId.Should().Be(c.Mensagem.MensagemId);
        c.Item.QuantidadeDisponivel.Should().Be(7);
        c.Item.QuantidadeReservada.Should().Be(3);
        c.Estoque.Verify(repo => repo.AtualizarAsync(c.Dados.FilialId, c.Saldo, default), Times.Once);
        c.Sessao.Verify(sessao => sessao.RegistrarResultadoAsync(c.Mensagem, 1, It.IsAny<string>(), resultado.Resultado, default), Times.Once);
        c.Sessao.Verify(sessao => sessao.SalvarAtualAsync(c.Mensagem.OrdemServicoId, It.Is<PreparacaoAtual>(atual => atual.Ativa), default), Times.Once);
    }

    [Fact]
    public async Task Dado_EstoqueInsuficiente_Quando_Preparar_Entao_RecusaSemGravarReserva()
    {
        var c = new Cenario();
        var resultado = await c.Preparar.ExecuteAsync(new(c.Mensagem, c.Dados with { Itens = [new(c.ItemId, 11)] }));

        resultado.Resultado.Tipo.Should().Be("PreparacaoRecusada");
        c.Item.QuantidadeDisponivel.Should().Be(10);
        c.Estoque.Verify(repo => repo.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<Estoque>(), default), Times.Never);
        c.Sessao.Verify(sessao => sessao.SalvarReservaAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<ReservaPersistida>(), It.IsAny<bool>(), default), Times.Never);
        c.Sessao.Verify(sessao => sessao.RegistrarResultadoAsync(c.Mensagem, 1, It.IsAny<string>(), resultado.Resultado, default), Times.Once);
    }

    [Fact]
    public async Task Dado_ResultadoAnterior_Quando_PrepararNovamente_Entao_NaoConsultaNemAlteraEstoque()
    {
        var c = new Cenario();
        c.Sessao.Setup(sessao => sessao.ObterResultadoAsync(c.Mensagem, 1, It.IsAny<string>(), default)).ReturnsAsync(c.Mensagem);

        var resultado = await c.Preparar.ExecuteAsync(new(c.Mensagem, c.Dados));

        resultado.Resultado.Should().BeSameAs(c.Mensagem);
        c.Estoque.VerifyNoOtherCalls();
        c.Sessao.Verify(sessao => sessao.RegistrarResultadoAsync(It.IsAny<Mensagem>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Mensagem>(), default), Times.Never);
    }

    [Theory]
    [InlineData(1, true, 2, "PREPARACAO_ANTERIOR_ATIVA")]
    [InlineData(2, false, 2, "VERSAO_PREPARACAO_INVALIDA")]
    [InlineData(2, false, 1, "VERSAO_PREPARACAO_INVALIDA")]
    public async Task Dado_PreparacaoAnterior_Quando_VersaoOuEstadoInvalido_Entao_NaoMovimentaEstoque(int versaoAtual, bool ativa, int novaVersao, string codigo)
    {
        var c = new Cenario();
        c.Sessao.Setup(sessao => sessao.ObterAtualAsync(c.Mensagem.OrdemServicoId, default)).ReturnsAsync(new PreparacaoAtual(versaoAtual, Guid.NewGuid(), ativa));

        var executar = () => c.Preparar.ExecuteAsync(new(c.Mensagem, c.Dados with { Versao = novaVersao }));

        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage(codigo);
        c.Estoque.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_QuantidadeInvalida_Quando_Preparar_Entao_RejeitaAntesDaUnidadeDeTrabalho()
    {
        var c = new Cenario();
        var executar = () => c.Preparar.ExecuteAsync(new(c.Mensagem, c.Dados with { Itens = [new(c.ItemId, 0)] }));

        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage("PREPARACAO_INVALIDA");
        c.Unidade.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_ReservaConfirmada_Quando_Cancelar_Entao_EstornaEMarcaPreparacaoInativa()
    {
        var c = new Cenario();
        c.PrepararCancelamento();

        var resultado = await c.Cancelar.ExecuteAsync(new(c.Mensagem, new(c.PreparacaoId, "Orçamento recusado")));

        resultado.Resultado.Tipo.Should().Be("PreparacaoCancelada");
        c.Item.QuantidadeDisponivel.Should().Be(10);
        c.Item.QuantidadeReservada.Should().Be(0);
        c.Sessao.Verify(sessao => sessao.SalvarReservaAsync(c.Mensagem.OrdemServicoId, 1, It.Is<ReservaPersistida>(reserva => reserva.Liberada), false, default), Times.Once);
        c.Sessao.Verify(sessao => sessao.SalvarPreparacaoAsync(c.Mensagem.OrdemServicoId, 1, It.Is<EstadoPreparacao>(estado => estado.Cancelada), default), Times.Once);
        c.Sessao.Verify(sessao => sessao.SalvarAtualAsync(c.Mensagem.OrdemServicoId, It.Is<PreparacaoAtual>(atual => !atual.Ativa), default), Times.Once);
        c.Sessao.Verify(sessao => sessao.RegistrarResultadoAsync(c.Mensagem, 1, It.IsAny<string>(), resultado.Resultado, default), Times.Once);
    }

    [Fact]
    public async Task Dado_CancelamentoJaConfirmado_Quando_Reentregar_Entao_NaoLiberaNovamente()
    {
        var c = new Cenario();
        c.PrepararCancelamento();
        c.Sessao.Setup(sessao => sessao.ObterResultadoAsync(c.Mensagem, 1, It.IsAny<string>(), default)).ReturnsAsync(c.Mensagem);

        var resultado = await c.Cancelar.ExecuteAsync(new(c.Mensagem, new(c.PreparacaoId, "Recusa")));

        resultado.Resultado.Should().BeSameAs(c.Mensagem);
        c.Item.QuantidadeReservada.Should().Be(3);
        c.Estoque.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_PreparacaoDesconhecida_Quando_Cancelar_Entao_NaoAnunciaCompensacao()
    {
        var c = new Cenario();
        var executar = () => c.Cancelar.ExecuteAsync(new(c.Mensagem, new(c.PreparacaoId, "Recusa")));

        await executar.Should().ThrowAsync<PreparacaoNaoEncontradaException>();
        c.Estoque.VerifyNoOtherCalls();
        c.Sessao.Verify(sessao => sessao.RegistrarResultadoAsync(It.IsAny<Mensagem>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Mensagem>(), default), Times.Never);
    }

    [Fact]
    public async Task Dado_ReservaPersistidaDivergente_Quando_Cancelar_Entao_NaoConfirmaCompensacao()
    {
        var c = new Cenario();
        c.PrepararCancelamento();
        c.Sessao.Setup(sessao => sessao.ObterReservaAsync(c.Mensagem.OrdemServicoId, c.ItemId, 1, default)).ReturnsAsync(new ReservaPersistida(c.PreparacaoId, c.Dados.FilialId, c.ItemId, 2, false));
        var executar = () => c.Cancelar.ExecuteAsync(new(c.Mensagem, new(c.PreparacaoId, "Recusa")));

        await executar.Should().ThrowAsync<DomainException>();
        c.Item.QuantidadeReservada.Should().Be(3);
        c.Estoque.VerifyNoOtherCalls();
        c.Sessao.Verify(sessao => sessao.RegistrarResultadoAsync(It.IsAny<Mensagem>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Mensagem>(), default), Times.Never);
    }

    [Fact]
    public async Task Dado_MotivoAusente_Quando_Cancelar_Entao_RejeitaAntesDaUnidadeDeTrabalho()
    {
        var c = new Cenario();
        var executar = () => c.Cancelar.ExecuteAsync(new(c.Mensagem, new(c.PreparacaoId, " ")));

        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage("CANCELAMENTO_INVALIDO");
        c.Unidade.VerifyNoOtherCalls();
    }

    private sealed class Cenario
    {
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid PreparacaoId { get; } = Guid.NewGuid();
        public Mensagem Mensagem { get; } = new(Guid.NewGuid(), "PrepararExecucao", 1, Guid.NewGuid(), Guid.NewGuid(), "teste", Guid.NewGuid(), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { }));
        public Application.Mensagens.PrepararExecucao Dados { get; }
        public ItemEstoque Item { get; }
        public Estoque Saldo { get; }
        public Mock<IEstoqueRepository> Estoque { get; } = new();
        public Mock<ISessaoPreparacao> Sessao { get; } = new();
        public Mock<IUnidadeTrabalhoPreparacao> Unidade { get; } = new();
        public PrepararExecucaoUseCase Preparar { get; }
        public CancelarPreparacaoUseCase Cancelar { get; }

        public Cenario()
        {
            Dados = new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [new(ItemId, 3)], [new(Guid.NewGuid(), "Troca de óleo", 125.50m)]);
            Item = ItemEstoque.Criar(ItemId, 10);
            Saldo = Domain.GestaoEstoque.Aggregates.Estoque.Criar([Item]);
            Sessao.SetupGet(sessao => sessao.Estoque).Returns(Estoque.Object);
            Estoque.Setup(repo => repo.ObterAsync(Dados.FilialId, It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(Saldo);
            Unidade.Setup(unidade => unidade.ExecutarAsync(It.IsAny<Func<ISessaoPreparacao, Task<PrepararExecucaoResponse>>>(), default))
                .Returns((Func<ISessaoPreparacao, Task<PrepararExecucaoResponse>> operacao, CancellationToken _) => operacao(Sessao.Object));
            Unidade.Setup(unidade => unidade.ExecutarAsync(It.IsAny<Func<ISessaoPreparacao, Task<CancelarPreparacaoResponse>>>(), default))
                .Returns((Func<ISessaoPreparacao, Task<CancelarPreparacaoResponse>> operacao, CancellationToken _) => operacao(Sessao.Object));
            Preparar = new(Unidade.Object, new PrepararExecucaoValidator(), TimeProvider.System);
            Cancelar = new(Unidade.Object, new CancelarPreparacaoValidator(), TimeProvider.System);
        }

        public void PrepararCancelamento()
        {
            Item.Reservar(3);
            Sessao.Setup(sessao => sessao.ObterReferenciaAsync(PreparacaoId, default)).ReturnsAsync(new ReferenciaPreparacao(Mensagem.OrdemServicoId, 1));
            Sessao.Setup(sessao => sessao.ObterAtualAsync(Mensagem.OrdemServicoId, default)).ReturnsAsync(new PreparacaoAtual(1, PreparacaoId, true));
            Sessao.Setup(sessao => sessao.ObterPreparacaoAsync(Mensagem.OrdemServicoId, 1, default)).ReturnsAsync(new EstadoPreparacao(PreparacaoId, Dados, true, false));
            Sessao.Setup(sessao => sessao.ObterReservaAsync(Mensagem.OrdemServicoId, ItemId, 1, default)).ReturnsAsync(new ReservaPersistida(PreparacaoId, Dados.FilialId, ItemId, 3, false));
        }
    }
}
