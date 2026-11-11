using Amazon;
using OficinaMecanica.Execucao.Infrastructure;
using OficinaMecanica.Execucao.Infrastructure.Hosting;

namespace OficinaMecanica.Execucao.API.Extensions.Configuration;

public sealed record ProcessamentoConfigurado(bool Remoto);

public static class ApiProcessingExtensions
{
    public static async Task<ClientesAwsAcademicos?> AddApiProcessingAsync(this WebApplicationBuilder builder,
        CancellationToken ct = default)
    {
        var config = builder.Configuration;
        if (!config.GetValue<bool>("ProcessamentoRemoto:Habilitado"))
        {
            builder.Services.AddInfrastructure(config);
            builder.Services.AddSingleton(new ProcessamentoConfigurado(false));
            return null;
        }
        if (config.GetValue<bool>("ProcessamentoLocal:Habilitado"))
            throw new InvalidOperationException("Processamento local e remoto não podem ser habilitados simultaneamente.");

        var secao = config.GetSection("ProcessamentoRemoto");
        var conta = secao["ContaEsperada"] ?? "";
        IdentidadeAcademica.ValidarConta(conta);
        var nomeRegiao = secao["Regiao"];
        var regiao = RegionEndpoint.EnumerableAllRegions.SingleOrDefault(r => r.SystemName == nomeRegiao)
            ?? throw new ArgumentException("Configure a região AWS do processamento remoto.");
        var options = new ProcessamentoRemotoOptions(secao["Tabela"] ?? "", secao["FilaEntradaUrl"] ?? "",
            secao["FilaResultadosOsUrl"] ?? "");
        options.Validar();
        ValidarFila(options.FilaEntradaUrl, conta, regiao.SystemName);
        ValidarFila(options.FilaResultadosOsUrl, conta, regiao.SystemName);

        var clientes = await ClientesAwsAcademicos.CriarAsync(regiao, conta, ct);
        try
        {
            builder.Services.AddInfrastructureRemota(options, clientes.DynamoDb, clientes.Sqs);
            builder.Services.AddSingleton(new ProcessamentoConfigurado(true));
            return clientes;
        }
        catch { clientes.Dispose(); throw; }
    }

    private static void ValidarFila(string url, string conta, string regiao)
    {
        var uri = new Uri(url);
        var partes = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host != $"sqs.{regiao}.amazonaws.com" || !uri.IsDefaultPort || partes.Length != 2 || partes[0] != conta)
            throw new ArgumentException("As filas devem pertencer à conta e região configuradas.");
    }
}
