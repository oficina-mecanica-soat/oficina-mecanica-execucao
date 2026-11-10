using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Messages;
using OficinaMecanica.Execucao.Domain.Preparacoes;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Domain.UnitTests.Preparacoes;

public sealed class PreparacaoEstoqueTests
{
    [Fact]
    public void Dado_DoisItensDisponiveis_Quando_ReservarECancelarDuasVezes_Entao_LiberaExatamenteUmaVez()
    {
        var primeiro = ItemEstoque.Criar(Guid.NewGuid(), 10);
        var segundo = ItemEstoque.Criar(Guid.NewGuid(), 5);
        var saldos = Saldos(primeiro, segundo);
        var preparacao = Reservar([new(primeiro.PecaInsumoCatalogoId, 3), new(segundo.PecaInsumoCatalogoId, 2)], saldos);

        Assert.Equal(7, primeiro.QuantidadeDisponivel);
        Assert.Equal(3, primeiro.QuantidadeReservada);
        Assert.Equal(3, segundo.QuantidadeDisponivel);
        Assert.Equal(2, segundo.QuantidadeReservada);
        Assert.True(preparacao.Cancelar(saldos));
        Assert.False(preparacao.Cancelar(saldos));
        Assert.True(preparacao.Cancelada);
        Assert.Equal(10, primeiro.QuantidadeDisponivel);
        Assert.Equal(5, segundo.QuantidadeDisponivel);
        Assert.Equal(0, primeiro.QuantidadeReservada);
        Assert.Equal(0, segundo.QuantidadeReservada);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dado_SegundoItemIndisponivelOuAusente_Quando_Reservar_Entao_NaoReservaParcialmente(bool existente)
    {
        var primeiro = ItemEstoque.Criar(Guid.NewGuid(), 10);
        var segundo = ItemEstoque.Criar(Guid.NewGuid(), 1);
        var saldos = existente ? Saldos(primeiro, segundo) : Saldos(primeiro);

        var exception = Assert.Throws<DomainException>(() =>
            Reservar([new(primeiro.PecaInsumoCatalogoId, 3), new(segundo.PecaInsumoCatalogoId, 2)], saldos));

        Assert.Equal(EstoqueErrorMessages.EstoqueInsuficiente, exception.Message);
        Assert.Equal(10, primeiro.QuantidadeDisponivel);
        Assert.Equal(0, primeiro.QuantidadeReservada);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dado_FalhaEmUmSaldo_Quando_Cancelar_Entao_NaoLiberaParcialmente(bool itemAusente)
    {
        var primeiro = ItemEstoque.Criar(Guid.NewGuid(), 10);
        var segundo = ItemEstoque.Criar(Guid.NewGuid(), 5);
        var saldos = Saldos(primeiro, segundo);
        var preparacao = Reservar([new(primeiro.PecaInsumoCatalogoId, 3), new(segundo.PecaInsumoCatalogoId, 2)], saldos);
        if (itemAusente)
            saldos.Remove(segundo.PecaInsumoCatalogoId);
        else
            segundo.Baixar(2);

        Assert.Throws<DomainException>(() => preparacao.Cancelar(saldos));

        Assert.False(preparacao.Cancelada);
        Assert.Equal(7, primeiro.QuantidadeDisponivel);
        Assert.Equal(3, primeiro.QuantidadeReservada);
    }

    [Fact]
    public void Dado_SemPecas_Quando_PrepararECancelar_Entao_ConfirmaSemAlterarSaldo()
    {
        var preparacao = Reservar([], Saldos());

        Assert.NotEqual(Guid.Empty, preparacao.PreparacaoId);
        Assert.NotEqual(Guid.Empty, preparacao.OrdemServicoId);
        Assert.NotEqual(Guid.Empty, preparacao.FilialId);
        Assert.Empty(preparacao.Itens);
        Assert.True(preparacao.Cancelar(Saldos()));
        Assert.False(preparacao.Cancelar(Saldos()));
    }

    [Fact]
    public void Dado_ItensRepetidos_Quando_Reservar_Entao_RejeitaSemAlterarSaldo()
    {
        var saldo = ItemEstoque.Criar(Guid.NewGuid(), 10);

        Assert.Throws<DomainException>(() => Reservar(
            [new(saldo.PecaInsumoCatalogoId, 3), new(saldo.PecaInsumoCatalogoId, 2)], Saldos(saldo)));

        Assert.Equal(10, saldo.QuantidadeDisponivel);
    }

    [Fact]
    public void Dado_ListaOriginalAlterada_Quando_ConsultarReserva_Entao_PreservaSnapshot()
    {
        var saldo = ItemEstoque.Criar(Guid.NewGuid(), 10);
        var itens = new List<ItemReserva> { new(saldo.PecaInsumoCatalogoId, 2) };
        var preparacao = Reservar(itens, Saldos(saldo));

        itens.Clear();

        Assert.Single(preparacao.Itens);
        Assert.Throws<NotSupportedException>(() => ((IList<ItemReserva>)preparacao.Itens).Clear());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Dado_QuantidadeInvalida_Quando_CriarReserva_Entao_Rejeita(int quantidade)
    {
        Assert.Throws<DomainException>(() => new ItemReserva(Guid.NewGuid(), quantidade));
    }

    [Fact]
    public void Dado_ItemSemIdentidade_Quando_CriarReserva_Entao_Rejeita()
    {
        Assert.Throws<DomainException>(() => new ItemReserva(Guid.Empty, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Dado_IdentificadorVazio_Quando_Preparar_Entao_Rejeita(int indice)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ids[indice] = Guid.Empty;

        Assert.Throws<DomainException>(() => PreparacaoEstoque.Reservar(ids[0], ids[1], ids[2], [], Saldos()));
    }

    [Fact]
    public void Dado_ItemNulo_Quando_Preparar_Entao_Rejeita()
    {
        Assert.Throws<DomainException>(() => Reservar([null!], Saldos()));
    }

    [Fact]
    public void Dado_ArgumentosNulos_Quando_PrepararOuCancelar_Entao_Rejeita()
    {
        Assert.Throws<ArgumentNullException>(() => Reservar(null!, Saldos()));
        Assert.Throws<ArgumentNullException>(() => Reservar([], null!));
        var preparacao = Reservar([], Saldos());
        Assert.Throws<ArgumentNullException>(() => preparacao.Cancelar(null!));
    }

    [Fact]
    public void Dado_CompensacaoInterrompida_Quando_RestaurarSaldoERepetir_Entao_LiberaUmaVez()
    {
        var saldo = ItemEstoque.Criar(Guid.NewGuid(), 10);
        var preparacao = Reservar([new(saldo.PecaInsumoCatalogoId, 3)], Saldos(saldo));
        var saldos = Saldos();
        Assert.Throws<DomainException>(() => preparacao.Cancelar(saldos));

        saldos.Add(saldo.PecaInsumoCatalogoId, saldo);

        Assert.True(preparacao.Cancelar(saldos));
        Assert.False(preparacao.Cancelar(saldos));
        Assert.Equal(10, saldo.QuantidadeDisponivel);
        Assert.Equal(0, saldo.QuantidadeReservada);
    }

    [Fact]
    public void Dado_LimiteDeReservas_Quando_Preparar_Entao_RejeitaSemCorromperSaldo()
    {
        var saldo = ItemEstoque.Criar(Guid.NewGuid(), int.MaxValue);
        saldo.Reservar(int.MaxValue);
        saldo.RegistrarEntrada(1);

        Assert.Throws<DomainException>(() => Reservar([new(saldo.PecaInsumoCatalogoId, 1)], Saldos(saldo)));

        Assert.Equal(1, saldo.QuantidadeDisponivel);
        Assert.Equal(int.MaxValue, saldo.QuantidadeReservada);
    }

    [Fact]
    public void Dado_LimiteDeDisponibilidade_Quando_Cancelar_Entao_MantemCompensacaoPendente()
    {
        var saldo = ItemEstoque.Criar(Guid.NewGuid(), 1);
        var preparacao = Reservar([new(saldo.PecaInsumoCatalogoId, 1)], Saldos(saldo));
        saldo.AtualizarQuantidadeDisponivel(int.MaxValue);

        Assert.Throws<DomainException>(() => preparacao.Cancelar(Saldos(saldo)));

        Assert.False(preparacao.Cancelada);
        Assert.Equal(1, saldo.QuantidadeReservada);
        Assert.Equal(int.MaxValue, saldo.QuantidadeDisponivel);
    }

    private static PreparacaoEstoque Reservar(IEnumerable<ItemReserva> itens, IReadOnlyDictionary<Guid, ItemEstoque> saldos)
        => PreparacaoEstoque.Reservar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), itens, saldos);

    private static Dictionary<Guid, ItemEstoque> Saldos(params ItemEstoque[] itens)
        => itens.ToDictionary(item => item.PecaInsumoCatalogoId);
}
