using System.Net;
using Amazon;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Moq;
using OficinaMecanica.Execucao.Infrastructure.Hosting;

namespace OficinaMecanica.Execucao.Infrastructure.UnitTests;

public sealed class IdentidadeAcademicaTests
{
    private const string Conta = "123456789012";
    private const string Arn = "arn:aws:sts::123456789012:assumed-role/LabRole/sessao-academica";

    [Fact]
    public async Task Dado_LabRoleDaContaEsperada_Quando_Validar_Entao_AceitaSemConsultarOutrosServicos()
    {
        var sts = CriarSts(Conta, Arn);
        using var cts = new CancellationTokenSource();
        await IdentidadeAcademica.ValidarAsync(sts.Object, Conta, cts.Token);
        sts.Verify(s => s.GetCallerIdentityAsync(It.IsAny<GetCallerIdentityRequest>(), cts.Token), Times.Once);
        sts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("999999999999", Arn)]
    [InlineData(Conta, "arn:aws:sts::999999999999:assumed-role/LabRole/sessao")]
    [InlineData(Conta, "arn:aws:sts::123456789012:assumed-role/voclabs/sessao")]
    [InlineData(Conta, "arn:aws:sts::123456789012:assumed-role/LabRoleOutra/sessao")]
    [InlineData(Conta, "arn:aws:iam::123456789012:user/LabRole")]
    [InlineData(Conta, "arn:aws:iam::123456789012:role/LabRole")]
    [InlineData(Conta, "arn:aws:sts::123456789012:assumed-role/LabRole/")]
    [InlineData(Conta, "arn:aws:sts::123456789012:assumed-role/LabRole/sessao/extra")]
    [InlineData(Conta, "arn:aws:sts::123456789012:assumed-role/LabRole/sessao\n")]
    [InlineData(Conta, null)]
    public async Task Dado_IdentidadeDivergente_Quando_Validar_Entao_BloqueiaSemExporResposta(string conta, string? arn)
    {
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => IdentidadeAcademica.ValidarAsync(CriarSts(conta, arn).Object, Conta));
        Assert.DoesNotContain(Conta, erro.Message);
        Assert.DoesNotContain("arn:", erro.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345678901x")]
    public async Task Dado_ContaInvalida_Quando_CriarOuValidar_Entao_FalhaAntesDeObterCredenciais(string? conta)
    {
        var sts = new Mock<IAmazonSecurityTokenService>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ArgumentException>(() => IdentidadeAcademica.ValidarAsync(sts.Object, conta!));
        await Assert.ThrowsAsync<ArgumentException>(() => ClientesAwsAcademicos.CriarAsync(RegionEndpoint.USEast1, conta!));
        sts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_StsSemConfirmacao_Quando_Validar_Entao_NaoAceitaIdentidade()
    {
        var sts = CriarSts(Conta, Arn, HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => IdentidadeAcademica.ValidarAsync(sts.Object, Conta));
    }

    [Fact]
    public async Task Dado_FalhaDoSts_Quando_Validar_Entao_PropagaSemFallback()
    {
        var sts = new Mock<IAmazonSecurityTokenService>(MockBehavior.Strict);
        var falha = new AmazonSecurityTokenServiceException("Falha simulada");
        sts.Setup(s => s.GetCallerIdentityAsync(It.IsAny<GetCallerIdentityRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(falha);
        Assert.Same(falha, await Assert.ThrowsAsync<AmazonSecurityTokenServiceException>(() => IdentidadeAcademica.ValidarAsync(sts.Object, Conta)));
    }

    [Fact]
    public async Task Dado_Cancelamento_Quando_CriarClientes_Entao_NaoConsultaMetadados()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientesAwsAcademicos.CriarAsync(RegionEndpoint.USEast1, Conta, cts.Token));
    }

    private static Mock<IAmazonSecurityTokenService> CriarSts(string conta, string? arn, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sts = new Mock<IAmazonSecurityTokenService>(MockBehavior.Strict);
        sts.Setup(s => s.GetCallerIdentityAsync(It.IsAny<GetCallerIdentityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetCallerIdentityResponse { Account = conta, Arn = arn, HttpStatusCode = status });
        return sts;
    }
}
