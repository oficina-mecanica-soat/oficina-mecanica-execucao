using System.Net;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;

namespace OficinaMecanica.Execucao.Infrastructure.Hosting;

public static class IdentidadeAcademica
{
    public static void ValidarConta(string contaEsperada)
    {
        if (string.IsNullOrEmpty(contaEsperada) || contaEsperada.Length != 12
            || contaEsperada.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Informe a conta AWS esperada com 12 dígitos.", nameof(contaEsperada));
    }

    public static async Task ValidarAsync(IAmazonSecurityTokenService sts, string contaEsperada, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sts);
        ValidarConta(contaEsperada);
        var resposta = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);
        var prefixo = $"arn:aws:sts::{contaEsperada}:assumed-role/LabRole/";
        var arn = resposta?.Arn;
        if (resposta?.HttpStatusCode != HttpStatusCode.OK || resposta.Account != contaEsperada
            || string.IsNullOrEmpty(arn) || !arn.StartsWith(prefixo, StringComparison.Ordinal)
            || arn.Length == prefixo.Length || arn[prefixo.Length..].Any(c => c == '/' || char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new InvalidOperationException("Identidade acadêmica do workload não corresponde à conta e LabRole esperadas.");
    }
}
