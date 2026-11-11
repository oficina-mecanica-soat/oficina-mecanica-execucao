using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Extensions.Responses;
using OficinaMecanica.Execucao.API.Extensions.Configuration;
using OficinaMecanica.Execucao.API.Filters;
using OficinaMecanica.Execucao.API.Middlewares;
using OficinaMecanica.Execucao.API.Responses;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiObservability();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Secret)
        && Encoding.UTF8.GetByteCount(options.Secret) >= 32
        && !string.IsNullOrWhiteSpace(options.Issuer)
        && !string.IsNullOrWhiteSpace(options.Audience),
        "Configure issuer, audience e chave JWT com pelo menos 32 bytes.")
    .ValidateOnStart();

builder.Services.AddHealthChecks();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = _ =>
    new BadRequestObjectResult(new ErrorResponse(ApiResponseMessages.RequisicaoInvalida, TipoErro.Validacao)));
builder.Services.AddScoped<DisponibilidadeProcessamentoFilter>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, settings) =>
    {
        var jwt = settings.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RoleClaimType = "role",
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return context.Response.WriteApiErrorResponseAsJsonAsync(new ErrorResponse(ApiResponseMessages.NaoAutorizado, TipoErro.NaoAutorizado));
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteApiErrorResponseAsJsonAsync(new ErrorResponse(ApiResponseMessages.AcessoProibido, TipoErro.AcessoProibido));
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddApplication();
using var clientesAws = await builder.AddApiProcessingAsync();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapControllers();
await app.RunAsync();

public partial class Program;

public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
