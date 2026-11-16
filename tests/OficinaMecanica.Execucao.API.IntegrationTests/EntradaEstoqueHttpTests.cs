using System.Net;
using System.Net.Http.Json;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.RegistrarEntradaEstoque;
using OficinaMecanica.Execucao.Application.GestaoEstoque.EstoqueUseCases.Responses;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class EntradaEstoqueHttpTests(HttpDynamoDbFixture fixture) : IClassFixture<HttpDynamoDbFixture>
{
    private const string Rota = "/api/v1/execucoes/estoque/registrar-entrada";

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Cliente", 403)]
    [InlineData("Mecanico", 403)]
    public async Task Dado_PerfilSemPermissao_Quando_RegistrarEntrada_Entao_BloqueiaAntesDaPersistencia(string? perfil, int status)
    {
        // Arrange
        await using var factory = ExecucoesHttpFactory.Criar();
        using var client = factory.CreateClient();
        if (perfil is not null) ExecucoesHttpFactory.Autenticar(client, perfil);

        // Act
        using var response = await client.PostAsJsonAsync(Rota, new RegistrarEntradaEstoqueRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10));

        // Assert
        Assert.Equal(status, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Atendente")]
    public async Task Dado_EntradaValida_Quando_RepetirHttp_Entao_SomaUmaVezERejeitaConteudoDivergente(string perfil)
    {
        // Arrange
        var tabela = "entrada-http-" + Guid.NewGuid().ToString("N");
        using var dynamo = fixture.CriarClient();
        await DynamoDbLocal.PrepararTabelaAsync(dynamo, tabela, default);
        await using var factory = ExecucoesHttpFactory.Criar(habilitado: true, endpoint: fixture.Endpoint, tabela: tabela);
        using var client = factory.CreateClient();
        ExecucoesHttpFactory.Autenticar(client, perfil);
        var request = new RegistrarEntradaEstoqueRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10);

        // Act
        using var primeira = await client.PostAsJsonAsync(Rota, request);
        using var repetida = await client.PostAsJsonAsync(Rota, request);
        using var divergente = await client.PostAsJsonAsync(Rota, request with { Quantidade = 20 });
        using var invalida = await client.PostAsJsonAsync(Rota, request with { OperacaoId = Guid.NewGuid(), Quantidade = 0 });

        // Assert
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(await primeira.Content.ReadFromJsonAsync<ItemEstoqueResponse>(), await repetida.Content.ReadFromJsonAsync<ItemEstoqueResponse>());
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }
}
