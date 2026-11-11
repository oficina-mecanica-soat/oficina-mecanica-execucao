using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OficinaMecanica.Execucao.Infrastructure.Hosting;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

internal static class ExecucoesHttpFactory
{
    public static string Secret => new('a', 32);
    public static WebApplicationFactory<Program> Criar(string ambiente = "Development", bool habilitado = false,
        string endpoint = "https://example.invalid", string tabela = "teste-http", Action<IServiceCollection>? configurar = null)
        => InicializacaoTests.CriarFactory(Secret).WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(ambiente);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProcessamentoLocal:Habilitado"] = habilitado.ToString(),
                ["ProcessamentoLocal:Endpoint"] = endpoint,
                ["ProcessamentoLocal:Tabela"] = tabela
            }));
            builder.ConfigureTestServices(services =>
            {
                // A suíte HTTP exercita a persistência real, isolando somente o transporte por arquivos.
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(ProcessamentoLocalWorker)).ToArray()) services.Remove(descriptor);
                configurar?.Invoke(services);
            });
        });

    public static void Autenticar(HttpClient client, string perfil, string issuer = "oficina-mecanica-auth",
        string audience = "oficina-mecanica-api", int minutos = 10, bool assinaturaValida = true)
    {
        var token = new JwtSecurityToken(issuer, audience,
            [new Claim("sub", Guid.NewGuid().ToString("D")), new Claim("role", perfil)],
            expires: DateTime.UtcNow.AddMinutes(minutos), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(assinaturaValida ? Secret : new string('b', 32))), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
