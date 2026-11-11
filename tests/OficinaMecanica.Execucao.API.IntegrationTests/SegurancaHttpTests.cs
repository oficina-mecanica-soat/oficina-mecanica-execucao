using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OficinaMecanica.Execucao.Application.Preparacoes;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class SegurancaHttpTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("token-invalido", 401)]
    [InlineData("Cliente", 403)]
    [InlineData("Atendente", 403)]
    [InlineData("Outro", 403)]
    public async Task Dado_IdentidadeSemAcesso_Quando_UsarQualquerRota_Entao_BloqueiaAntesDaPersistencia(string? perfil, int status)
    {
        await using var factory = ExecucoesHttpFactory.Criar(); // Endpoint inválido prova que não se resolve o client.
        using var client = factory.CreateClient();
        if (perfil == "token-invalido") client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer token-invalido");
        else if (perfil is not null) ExecucoesHttpFactory.Autenticar(client, perfil);
        foreach (var (metodo, sufixo) in Rotas())
        {
            using var response = await EnviarAsync(client, metodo, sufixo);
            Assert.Equal(status, (int)response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(status == 401 ? "NaoAutorizado" : "AcessoProibido", body.GetProperty("tipo").GetString());
        }
    }

    [Theory]
    [InlineData("outro-emissor", "oficina-mecanica-api", 10, true)]
    [InlineData("oficina-mecanica-auth", "outro-publico", 10, true)]
    [InlineData("oficina-mecanica-auth", "oficina-mecanica-api", -10, true)]
    [InlineData("oficina-mecanica-auth", "oficina-mecanica-api", 10, false)]
    public async Task Dado_JwtInvalido_Quando_AcionarHttp_Entao_Retorna401(string issuer, string audience, int minutos, bool assinatura)
    {
        await using var factory = ExecucoesHttpFactory.Criar();
        using var client = factory.CreateClient();
        ExecucoesHttpFactory.Autenticar(client, "Mecanico", issuer, audience, minutos, assinatura);
        using var response = await EnviarAsync(client, HttpMethod.Post, "/finalizar");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Dado_RuntimeNaoConfigurado_Quando_UsuarioAutorizadoAcionar_Entao_Retorna503SemFallbackLocal(string ambiente)
    {
        await using var factory = ExecucoesHttpFactory.Criar(ambiente);
        using var client = factory.CreateClient();
        ExecucoesHttpFactory.Autenticar(client, "Mecanico");
        foreach (var (metodo, sufixo) in Rotas())
        {
            using var response = await EnviarAsync(client, metodo, sufixo);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        Assert.Equal("Healthy", await client.GetStringAsync("/api/health"));
    }

    [Fact]
    public async Task Dado_FalhaTecnica_Quando_ProcessarHttp_Entao_NaoExibeDetalheSensivelNemRetorna202()
    {
        var unidade = new UnidadeComFalha();
        await using var factory = ExecucoesHttpFactory.Criar(habilitado: true, configurar: services =>
        {
            services.RemoveAll<IUnidadeTrabalhoPreparacao>();
            services.AddSingleton<IUnidadeTrabalhoPreparacao>(unidade);
        });
        using var client = factory.CreateClient();
        ExecucoesHttpFactory.Autenticar(client, "Mecanico");
        using var response = await EnviarAsync(client, HttpMethod.Post, "/finalizar");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("detalhe-sensivel-ficticio", body);
        Assert.Contains("Erro interno inesperado", body);
        Assert.Null(response.Headers.Location);
        Assert.True(unidade.Executou);
    }

    private static (HttpMethod Metodo, string Sufixo)[] Rotas() => [(HttpMethod.Get, ""),
        (HttpMethod.Post, $"/servicos/{Guid.NewGuid():D}/iniciar-execucao"),
        (HttpMethod.Post, $"/servicos/{Guid.NewGuid():D}/finalizar"), (HttpMethod.Post, "/finalizar")];

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient client, HttpMethod metodo, string sufixo)
    {
        var request = new HttpRequestMessage(metodo, $"/api/v1/execucoes/{Guid.NewGuid():D}/preparacoes/{Guid.NewGuid():D}{sufixo}");
        if (metodo == HttpMethod.Post) request.Content = JsonContent.Create(new { operacaoId = Guid.NewGuid() });
        return client.SendAsync(request);
    }

    private sealed class UnidadeComFalha : IUnidadeTrabalhoPreparacao
    {
        public bool Executou { get; private set; }
        public Task<T> ExecutarAsync<T>(Func<ISessaoPreparacao, Task<T>> operacao, CancellationToken ct)
        {
            Executou = true;
            throw new IOException("detalhe-sensivel-ficticio");
        }
    }
}
