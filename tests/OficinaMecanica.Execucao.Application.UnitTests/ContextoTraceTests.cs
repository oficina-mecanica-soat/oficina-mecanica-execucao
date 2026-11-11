using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.UnitTests;

public sealed class ContextoTraceTests
{
    private const string Valido = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    [Fact]
    public void Dado_ContextoComCamposExtras_Quando_Filtrar_Entao_PreservaSomenteTraceparentSemMutarOrigem()
    {
        var origem = new Dictionary<string, string> { ["traceparent"] = Valido, ["Authorization"] = "segredo-ficticio", ["baggage"] = "cpf=12345678901" };
        var filtrado = ContextoTrace.Filtrar(origem)!;
        Assert.Equal(Valido, Assert.Single(filtrado).Value);
        filtrado.Clear();
        Assert.Equal(3, origem.Count);
        Assert.Null(ContextoTrace.Filtrar(null));
        Assert.Null(ContextoTrace.Filtrar(new Dictionary<string, string> { ["baggage"] = "nao-permitido" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer token-ficticio")]
    [InlineData("00-00000000000000000000000000000000-00f067aa0ba902b7-01")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01")]
    [InlineData("ff-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-xx")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e473x-00f067aa0ba902b7-01")]
    public void Dado_TraceparentInvalido_Quando_Filtrar_Entao_DescartaSemInventarId(string valor)
        => Assert.Null(ContextoTrace.Filtrar(new Dictionary<string, string> { ["traceparent"] = valor }));
}
