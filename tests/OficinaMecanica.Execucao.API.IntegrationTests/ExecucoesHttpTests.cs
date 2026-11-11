using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class ExecucoesHttpTests(HttpDynamoDbFixture fixture) : IClassFixture<HttpDynamoDbFixture>
{
    [Theory]
    [InlineData("Mecanico")]
    [InlineData("Administrador")]
    public async Task Dado_PreparacaoAutorizada_Quando_MecanicoAcionarHttp_Entao_Confirma202AposCommitEPermiteConsulta(string perfil)
    {
        await using var c = await Cenario.CriarAsync(fixture, perfil);
        using (var pendente = await c.Client.GetAsync(c.Recurso))
        {
            Assert.Equal(HttpStatusCode.OK, pendente.StatusCode);
            var body = JsonDocument.Parse(await pendente.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(JsonValueKind.Null, body.GetProperty("versaoOrcamento").ValueKind);
        }
        using (var bloqueada = await c.AcaoAsync("iniciar-execucao")) Assert.Equal((HttpStatusCode)422, bloqueada.StatusCode);
        await c.AutorizarAsync();
        var id = Guid.NewGuid();
        using var iniciada = await c.AcaoAsync("iniciar-execucao", id);
        Assert.Equal(HttpStatusCode.Accepted, iniciada.StatusCode);
        Assert.Equal(c.Recurso, iniciada.Headers.Location!.AbsolutePath);
        Assert.Equal(id, JsonDocument.Parse(await iniciada.Content.ReadAsStringAsync()).RootElement.GetProperty("operacaoId").GetGuid());
        var primeiraData = (await c.EstadoAsync()).Execucao!.Servicos[0].DataInicio;
        using (var repetida = await c.AcaoAsync("iniciar-execucao", id)) Assert.Equal(HttpStatusCode.Accepted, repetida.StatusCode);
        Assert.Equal(primeiraData, (await c.EstadoAsync()).Execucao!.Servicos[0].DataInicio);
        using (var cedo = await c.FinalizarAsync()) Assert.Equal((HttpStatusCode)422, cedo.StatusCode);
        using (var concluida = await c.AcaoAsync("finalizar")) Assert.Equal(HttpStatusCode.Accepted, concluida.StatusCode);
        Assert.Equal(3, (await c.SaldoAsync()).Reservada);
        Assert.Null((await c.EstadoAsync()).Execucao!.FinalizadaEm);
        using (var final = await c.FinalizarAsync()) Assert.Equal(HttpStatusCode.Accepted, final.StatusCode);
        using (var repetida = await c.FinalizarAsync()) Assert.Equal(HttpStatusCode.Accepted, repetida.StatusCode);
        Assert.Equal(7, (await c.SaldoAsync()).Disponivel);
        Assert.Equal(0, (await c.SaldoAsync()).Reservada);
        using var consulta = await c.Client.GetAsync(iniciada.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        var json = await consulta.Content.ReadAsStringAsync();
        var estado = JsonDocument.Parse(json).RootElement;
        Assert.Equal(7, estado.GetProperty("versaoOrcamento").GetInt32());
        Assert.Equal("Finalizado", estado.GetProperty("execucao").GetProperty("servicos")[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, estado.GetProperty("execucao").GetProperty("finalizadaEm").ValueKind);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("origem", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"operacaoId\":\"invalido\"}")]
    [InlineData("{\"operacaoId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"operacaoId\":\"49b0ac8b-dcf8-40b9-ac75-e07fc4a4f8a0\",\"ordemServicoId\":\"nao-pode-sobrescrever-rota\"}")]
    public async Task Dado_CorpoInvalido_Quando_Acionar_Entao_Retorna400SemAlterarEstado(string body)
    {
        await using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        using var response = await c.Client.PostAsync(c.Recurso + $"/servicos/{c.ServicoId:D}/iniciar-execucao", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Validacao", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("tipo").GetString());
        Assert.Null((await c.EstadoAsync()).Execucao!.Servicos[0].DataInicio);
        Assert.Equal(3, (await c.SaldoAsync()).Reservada);
    }

    [Fact]
    public async Task Dado_RecursoInexistenteOuDeOutraOs_Quando_ConsultarOuAcionar_Entao_Retorna404()
    {
        await using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        foreach (var recurso in new[] { $"/api/v1/execucoes/{Guid.NewGuid():D}/preparacoes/{c.PreparacaoId:D}",
            $"/api/v1/execucoes/{c.OsId:D}/preparacoes/{Guid.NewGuid():D}" })
        {
            using var consulta = await c.Client.GetAsync(recurso);
            using var inicio = await c.Client.PostAsJsonAsync(recurso + $"/servicos/{c.ServicoId:D}/iniciar-execucao", new { operacaoId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.NotFound, consulta.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, inicio.StatusCode);
        }
        Assert.Null((await c.EstadoAsync()).Execucao!.Servicos[0].DataInicio);
    }

    [Fact]
    public async Task Dado_OperacaoReutilizadaComOutraAcao_Quando_Finalizar_Entao_Retorna409SemAlterarServico()
    {
        await using var c = await Cenario.CriarAsync(fixture);
        await c.AutorizarAsync();
        var id = Guid.NewGuid();
        using var inicio = await c.AcaoAsync("iniciar-execucao", id);
        using var fim = await c.AcaoAsync("finalizar", id);
        Assert.Equal(HttpStatusCode.Accepted, inicio.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, fim.StatusCode);
        Assert.Null((await c.EstadoAsync()).Execucao!.Servicos[0].DataFim);
    }

    [Fact]
    public async Task Dado_PreparacaoCancelada_Quando_Consultar_Entao_NaoExigeAutorizacaoDeExecucaoNemReabreEstado()
    {
        await using var c = await Cenario.CriarAsync(fixture);
        await c.ProcessarAsync(c.Comando("CancelarPreparacao", new CancelarPreparacao(c.PreparacaoId, "Recusa")));
        using var consulta = await c.Client.GetAsync(c.Recurso);
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        Assert.True(JsonDocument.Parse(await consulta.Content.ReadAsStringAsync()).RootElement.GetProperty("cancelada").GetBoolean());
        using var inicio = await c.AcaoAsync("iniciar-execucao");
        Assert.Equal((HttpStatusCode)422, inicio.StatusCode);
        Assert.Equal(10, (await c.SaldoAsync()).Disponivel);
        var snapshot = (await c.EstadoAsync()).Snapshot;
        await c.ProcessarAsync(c.Comando("PrepararExecucao", snapshot with { Versao = 2 }));
        using var historico = await c.Client.GetAsync(c.Recurso);
        Assert.Equal(HttpStatusCode.OK, historico.StatusCode);
        var body = JsonDocument.Parse(await historico.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("cancelada").GetBoolean());
        Assert.Equal(1, body.GetProperty("versaoPreparacao").GetInt32());
    }

    private sealed class Cenario(WebApplicationFactory<Program> factory, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public Guid OsId { get; } = Guid.NewGuid();
        public Guid FilialId { get; } = Guid.NewGuid();
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid ServicoId { get; } = Guid.NewGuid();
        public Guid PreparacaoId { get; private set; }
        public string Recurso => $"/api/v1/execucoes/{OsId:D}/preparacoes/{PreparacaoId:D}";
        public static async Task<Cenario> CriarAsync(HttpDynamoDbFixture fixture, string perfil = "Mecanico")
        {
            var tabela = "http-" + Guid.NewGuid().ToString("N");
            using var dynamo = fixture.CriarClient();
            await DynamoDbLocal.PrepararTabelaAsync(dynamo, tabela, default);
            var factory = ExecucoesHttpFactory.Criar(habilitado: true, endpoint: fixture.Endpoint, tabela: tabela);
            var client = factory.CreateClient();
            ExecucoesHttpFactory.Autenticar(client, perfil);
            var c = new Cenario(factory, client);
            using var scope = factory.Services.CreateScope();
            var tx = new TransacaoExecucao(scope.ServiceProvider.GetRequiredService<IArmazenamentoExecucao>());
            await tx.AdicionarAsync(ChavesExecucao.Estoque(c.FilialId, c.ItemId), SaldoEstoque.De(ItemEstoque.Criar(c.ItemId, 10)), default);
            await tx.ConfirmarAsync(default);
            var resultado = await c.ProcessarAsync(c.Comando("PrepararExecucao", new PrepararExecucao(1, c.FilialId,
                Guid.NewGuid(), Guid.NewGuid(), [new(c.ItemId, 3)], [new(c.ServicoId, "Troca de óleo", 10m, Guid.NewGuid())])));
            c.PreparacaoId = resultado!.Dados.Deserialize<ExecucaoPreparada>(JsonContrato.Options)!.PreparacaoId;
            return c;
        }
        public Mensagem Comando<T>(string tipo, T dados) => new(Guid.NewGuid(), tipo, 1, OsId, OsId, "teste-http",
            Guid.NewGuid(), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(dados, JsonContrato.Options));
        public async Task<Mensagem?> ProcessarAsync(Mensagem comando)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ProcessadorPreparacao>().ProcessarAsync(JsonContrato.Serializar(comando));
        }
        public Task<Mensagem?> AutorizarAsync() => ProcessarAsync(Comando("AutorizarExecucao", new AutorizarExecucao(PreparacaoId, 7)));
        public Task<HttpResponseMessage> AcaoAsync(string acao, Guid? operacao = null) => Client.PostAsJsonAsync(
            Recurso + $"/servicos/{ServicoId:D}/{acao}", new { operacaoId = operacao ?? Guid.NewGuid() });
        public Task<HttpResponseMessage> FinalizarAsync() => Client.PostAsJsonAsync(Recurso + "/finalizar", new { operacaoId = Guid.NewGuid() });
        public async Task<EstadoPreparacao> EstadoAsync()
        {
            using var scope = factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IArmazenamentoExecucao>().ObterAsync(ChavesExecucao.Preparacao(OsId, 1), default)).Ler<EstadoPreparacao>()!;
        }
        public async Task<SaldoEstoque> SaldoAsync()
        {
            using var scope = factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IArmazenamentoExecucao>().ObterAsync(ChavesExecucao.Estoque(FilialId, ItemId), default)).Ler<SaldoEstoque>()!;
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }
    }
}

public sealed class HttpDynamoDbFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("amazon/dynamodb-local:3.1.0")
        .WithPortBinding(8000, true).WithCommand("-jar", "DynamoDBLocal.jar", "-inMemory", "-sharedDb")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8000)).Build();
    public string Endpoint => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8000)}";
    public AmazonDynamoDBClient CriarClient() => DynamoDbLocal.CriarClient(Endpoint);
    public Task InitializeAsync() => _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}
