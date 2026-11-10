using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.Execucao.API.IntegrationTests;

public sealed class AutenticacaoTests
{
    [Theory]
    [InlineData("oficina-mecanica-auth", "oficina-mecanica-api", 10, true, true)]
    [InlineData("outro-emissor", "oficina-mecanica-api", 10, true, false)]
    [InlineData("oficina-mecanica-auth", "outro-publico", 10, true, false)]
    [InlineData("oficina-mecanica-auth", "oficina-mecanica-api", -10, true, false)]
    [InlineData("oficina-mecanica-auth", "oficina-mecanica-api", 10, false, false)]
    public async Task Dado_Jwt_Quando_Autenticar_Entao_ValidaContrato(
        string issuer, string audience, int minutos, bool assinaturaValida, bool esperado)
    {
        var secret = new string('a', 32);
        await using var factory = InicializacaoTests.CriarFactory(secret);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(assinaturaValida ? secret : new string('b', 32))),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience,
            [new Claim("sub", "identidade-teste"), new Claim("role", "Mecanico")],
            expires: DateTime.UtcNow.AddMinutes(minutos), signingCredentials: credentials);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer " + new JwtSecurityTokenHandler().WriteToken(token);

        var result = await context.AuthenticateAsync("Bearer");

        Assert.Equal(esperado, result.Succeeded);
        if (esperado)
        {
            Assert.True(result.Principal!.IsInRole("Mecanico"));
            Assert.Equal("identidade-teste", result.Principal.Identity!.Name);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer token-invalido")]
    public async Task Dado_TokenAusenteOuMalformado_Quando_Autenticar_Entao_NaoAutoriza(string? token)
    {
        await using var factory = InicializacaoTests.CriarFactory(new string('a', 32));
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (token is not null)
            context.Request.Headers.Authorization = token;

        var result = await context.AuthenticateAsync("Bearer");

        Assert.False(result.Succeeded);
    }
}
