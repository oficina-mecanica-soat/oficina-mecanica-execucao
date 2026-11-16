using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class EntradaEstoquePersistenteTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    [Fact]
    public async Task Dado_Entrada_Quando_RepetirAposReabrirSessao_Entao_SomaUmaVezEPreservaReserva()
    {
        // Arrange
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var request = new RegistrarEntradaEstoqueRequest(Guid.NewGuid(), cenario.FilialId, cenario.ItemId, 10);
        RegistrarEntradaEstoqueUseCase Criar() => new(new UnidadeTrabalhoEstoqueDynamoDb(cenario.Armazenamento), new RegistrarEntradaEstoqueValidator());

        // Act
        var primeira = await Criar().ExecuteAsync(request);
        var repetida = await Criar().ExecuteAsync(request);
        var entrada = await Criar().ExecuteAsync(request with { OperacaoId = Guid.NewGuid(), Quantidade = 3 });

        // Assert
        Assert.Equal(primeira, repetida);
        Assert.Equal(13, entrada.QuantidadeDisponivel);
        Assert.Equal(0, entrada.QuantidadeReservada);
        Assert.Equal(primeira.Id, entrada.Id);
        var saldo = (await cenario.Armazenamento.ObterAsync(ChavesExecucao.Estoque(cenario.FilialId, cenario.ItemId), default)).Ler<SaldoEstoque>()!;
        Assert.Equal(13, saldo.Disponivel);
    }

    [Fact]
    public async Task Dado_OperacaoRegistrada_Quando_ReutilizarComOutroConteudo_Entao_NaoAlteraSaldo()
    {
        // Arrange
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var useCase = new RegistrarEntradaEstoqueUseCase(new UnidadeTrabalhoEstoqueDynamoDb(cenario.Armazenamento), new RegistrarEntradaEstoqueValidator());
        var request = new RegistrarEntradaEstoqueRequest(Guid.NewGuid(), cenario.FilialId, cenario.ItemId, 10);
        await useCase.ExecuteAsync(request);

        // Act
        var erro = await Assert.ThrowsAsync<ContratoMensagemException>(() => useCase.ExecuteAsync(request with { Quantidade = 20 }));

        // Assert
        Assert.Equal("MENSAGEM_ID_REUTILIZADO", erro.Message);
        var saldo = (await cenario.Armazenamento.ObterAsync(ChavesExecucao.Estoque(cenario.FilialId, cenario.ItemId), default)).Ler<SaldoEstoque>()!;
        Assert.Equal(10, saldo.Disponivel);
    }

    [Fact]
    public async Task Dado_ReentregaConcorrente_Quando_ConfirmarMesmaEntrada_Entao_PersisteUmUnicoEfeito()
    {
        // Arrange
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var request = new RegistrarEntradaEstoqueRequest(Guid.NewGuid(), cenario.FilialId, cenario.ItemId, 10);
        Task<Application.GestaoEstoque.EstoqueUseCases.Responses.ItemEstoqueResponse> Executar() =>
            new RegistrarEntradaEstoqueUseCase(new UnidadeTrabalhoEstoqueDynamoDb(cenario.Armazenamento), new RegistrarEntradaEstoqueValidator()).ExecuteAsync(request);

        // Act
        var resultados = await Task.WhenAll(Executar(), Executar());

        // Assert
        Assert.Equal(resultados[0], resultados[1]);
        var saldo = (await cenario.Armazenamento.ObterAsync(ChavesExecucao.Estoque(cenario.FilialId, cenario.ItemId), default)).Ler<SaldoEstoque>()!;
        Assert.Equal(10, saldo.Disponivel);
    }
}
