using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;

namespace OficinaMecanica.Execucao.Application.UnitTests;

public sealed class EntradaEstoqueValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Dado_QuantidadeInvalida_Quando_Validar_Entao_RecusaEntrada(int quantidade)
    {
        // Arrange
        var request = new RegistrarEntradaEstoqueRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), quantidade);
        var validator = new RegistrarEntradaEstoqueValidator();

        // Act
        var resultado = validator.Validate(request);

        // Assert
        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Dado_IdentidadesVazias_Quando_Validar_Entao_RecusaTodas()
    {
        // Arrange
        var validator = new RegistrarEntradaEstoqueValidator();

        // Act
        var resultado = validator.Validate(new RegistrarEntradaEstoqueRequest(Guid.Empty, Guid.Empty, Guid.Empty, 1));

        // Assert
        Assert.Equal(3, resultado.Errors.Count);
    }
}
