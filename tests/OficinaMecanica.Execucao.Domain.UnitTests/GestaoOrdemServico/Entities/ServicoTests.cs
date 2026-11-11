using FluentAssertions;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Entities;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Messages;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Domain.UnitTests.GestaoOrdemServico.Entities;

public class ServicoTests
{
    [Fact]
    public void Dado_DadosValidos_Quando_CriarServico_Entao_DeveRegistrarServicoPendenteDeExecucao()
    {
        // Arrange
        var servicoCatalogoId = Guid.NewGuid();

        // Act
        var servico = Servico.Criar(servicoCatalogoId, 150m);

        // Assert
        servico.Id.Should().NotBeEmpty();
        servico.ServicoCatalogoId.Should().Be(servicoCatalogoId);
        servico.Valor.Should().Be(150m);
        servico.Status.Should().Be(StatusServico.Pendente);
        servico.DataInicio.Should().BeNull();
        servico.DataFim.Should().BeNull();
    }

    [Fact]
    public void Dado_ServicoCatalogoIdVazio_Quando_CriarServico_Entao_DeveLancarDomainException()
    {
        // Arrange
        var servicoCatalogoId = Guid.Empty;

        // Act
        var acao = () => Servico.Criar(servicoCatalogoId, 150m);

        // Assert
        acao.Should()
            .Throw<DomainException>()
            .WithMessage(OrdemServicoErrorMessages.ServicoCatalogoObrigatorio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Dado_ValorInvalido_Quando_CriarServico_Entao_DeveLancarDomainException(decimal valor)
    {
        // Arrange
        var servicoCatalogoId = Guid.NewGuid();

        // Act
        var acao = () => Servico.Criar(servicoCatalogoId, valor);

        // Assert
        acao.Should()
            .Throw<DomainException>()
            .WithMessage(OrdemServicoErrorMessages.ValorServicoMaiorQueZero);
    }

    [Fact]
    public void Dado_ServicoPendente_Quando_IniciarExecucao_Entao_DeveFicarEmExecucao()
    {
        // Arrange
        var servico = Servico.Criar(Guid.NewGuid(), 150m);

        // Act
        servico.IniciarExecucao();

        // Assert
        servico.Status.Should().Be(StatusServico.EmExecucao);
        servico.DataInicio.Should().NotBeNull();
        servico.DataFim.Should().BeNull();
    }

    [Fact]
    public void Dado_ServicoEmExecucao_Quando_IniciarExecucao_Entao_DeveLancarDomainException()
    {
        // Arrange
        var servico = CriarServicoEmExecucao();

        // Act
        Action acao = servico.IniciarExecucao;

        // Assert
        acao.Should()
            .Throw<DomainException>()
            .WithMessage(OrdemServicoErrorMessages.ServicoPendenteParaIniciarExecucao);
    }

    [Fact]
    public void Dado_ServicoFinalizado_Quando_IniciarExecucao_Entao_DeveLancarDomainException()
    {
        // Arrange
        var servico = CriarServicoFinalizado();

        // Act
        Action acao = servico.IniciarExecucao;

        // Assert
        acao.Should()
            .Throw<DomainException>()
            .WithMessage(OrdemServicoErrorMessages.ServicoPendenteParaIniciarExecucao);
    }

    [Fact]
    public void Dado_ServicoEmExecucao_Quando_Finalizar_Entao_DeveFicarFinalizado()
    {
        // Arrange
        var servico = CriarServicoEmExecucao();

        // Act
        servico.Finalizar();

        // Assert
        servico.Status.Should().Be(StatusServico.Finalizado);
        servico.DataInicio.Should().NotBeNull();
        servico.DataFim.Should().NotBeNull();
    }

    [Fact]
    public void Dado_ServicoPendente_Quando_Finalizar_Entao_DeveLancarDomainException()
    {
        // Arrange
        var servico = Servico.Criar(Guid.NewGuid(), 150m);

        // Act
        Action acao = servico.Finalizar;

        // Assert
        acao.Should()
            .Throw<DomainException>()
            .WithMessage(OrdemServicoErrorMessages.ServicoEmExecucaoParaFinalizar);
    }

    [Fact]
    public void Dado_ServicoFinalizado_Quando_Finalizar_Entao_DeveLancarDomainException()
    {
        // Arrange
        var servico = CriarServicoFinalizado();

        // Act
        Action acao = servico.Finalizar;

        // Assert
        acao.Should()
            .Throw<DomainException>()
            .WithMessage(OrdemServicoErrorMessages.ServicoEmExecucaoParaFinalizar);
    }
    private static Servico CriarServicoEmExecucao()
    {
        var servico = Servico.Criar(Guid.NewGuid(), 150m);
        servico.IniciarExecucao();
        return servico;
    }

    private static Servico CriarServicoFinalizado()
    {
        var servico = CriarServicoEmExecucao();
        servico.Finalizar();
        return servico;
    }
}
