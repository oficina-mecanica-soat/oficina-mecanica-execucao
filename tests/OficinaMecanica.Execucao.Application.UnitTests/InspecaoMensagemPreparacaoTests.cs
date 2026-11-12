using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.UnitTests;

public sealed class InspecaoMensagemPreparacaoTests
{
    [Theory]
    [InlineData("PrepararExecucao")]
    [InlineData("CancelarPreparacao")]
    [InlineData("AutorizarExecucao")]
    public async Task Dado_MetadadosDiferentes_Quando_Inspecionar_Entao_PreservaHashDeNegocio(string tipo)
    {
        var mensagem = Criar(tipo);
        var original = JsonContrato.Serializar(mensagem);
        var primeira = await InspecaoMensagemPreparacao.InspecionarAsync(original);
        var outra = mensagem with
        {
            MensagemId = Guid.NewGuid(),
            SagaId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CorrelationId = "outra",
            OcorridoEm = mensagem.OcorridoEm.AddHours(1),
            TraceContext = new() { ["traceparent"] = "contexto" }
        };
        var segunda = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(outra));

        primeira.HashNegocio.Should().Be(segunda.HashNegocio);
        primeira.HashNegocio.Should().NotBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))));
        JsonContrato.Serializar(primeira.Mensagem).Should().Be(original);
        var outraOs = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(mensagem with { OrdemServicoId = Guid.NewGuid() }));
        outraOs.HashNegocio.Should().NotBe(primeira.HashNegocio);
    }

    [Fact]
    public async Task Dado_SnapshotReordenado_Quando_Inspecionar_Entao_NormalizaSemAlterarCorpo()
    {
        var mensagem = Criar("PrepararExecucao");
        var dados = mensagem.Dados.Deserialize<PrepararExecucao>(JsonContrato.Options)!;
        var reordenada = mensagem with
        {
            Dados = JsonSerializer.SerializeToElement(dados with
            {
                Itens = dados.Itens.Reverse().ToArray(),
                Servicos = dados.Servicos.Reverse().Select(s => s with { Valor = 125.500m }).ToArray()
            }, JsonContrato.Options)
        };
        var original = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(mensagem));
        var outra = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(reordenada));
        outra.HashNegocio.Should().Be(original.HashNegocio);
        outra.Mensagem.Dados.GetRawText().Should().Be(reordenada.Dados.GetRawText());

        var alterada = mensagem with { Dados = JsonSerializer.SerializeToElement(dados with { Itens = [new(dados.Itens[0].ItemId, 9)] }, JsonContrato.Options) };
        (await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(alterada))).HashNegocio.Should().NotBe(original.HashNegocio);
    }

    [Theory]
    [InlineData("PrepararExecucao", "{}", "PREPARACAO_INVALIDA")]
    [InlineData("CancelarPreparacao", "{}", "CANCELAMENTO_INVALIDO")]
    [InlineData("AutorizarExecucao", "{}", "AUTORIZACAO_INVALIDA")]
    [InlineData("AutorizarExecucao", "{\"campoExtra\":1}", "DADOS_INVALIDOS")]
    [InlineData("Outro", "{}", "TIPO_DESCONHECIDO")]
    public async Task Dado_ContratoInvalido_Quando_Inspecionar_Entao_Rejeita(string tipo, string dados, string codigo)
    {
        var mensagem = Criar("AutorizarExecucao") with { Tipo = tipo, Dados = JsonDocument.Parse(dados).RootElement.Clone() };
        var executar = () => InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(mensagem));
        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage(codigo);
    }

    [Theory]
    [InlineData("null", "JSON_NULO")]
    [InlineData("{", "JSON_INVALIDO")]
    [InlineData("{}", "ENVELOPE_INVALIDO")]
    public async Task Dado_EnvelopeInvalido_Quando_Inspecionar_Entao_Rejeita(string json, string codigo)
    {
        var executar = () => InspecaoMensagemPreparacao.InspecionarAsync(json);
        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage(codigo);
    }

    private static Mensagem Criar(string tipo)
    {
        object dados = tipo switch
        {
            "PrepararExecucao" => new PrepararExecucao(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                [new(Guid.NewGuid(), 1), new(Guid.NewGuid(), 2)],
                [new(Guid.NewGuid(), "Serviço A", 125.50m, Guid.NewGuid()), new(Guid.NewGuid(), "Serviço B", 125.50m, Guid.NewGuid())]),
            "CancelarPreparacao" => new CancelarPreparacao(Guid.NewGuid(), "Recusa"),
            _ => new AutorizarExecucao(Guid.NewGuid(), 1)
        };
        return new(Guid.NewGuid(), tipo, 1, Guid.NewGuid(), Guid.NewGuid(), "inspecao", Guid.NewGuid(),
            DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(dados, JsonContrato.Options));
    }
}
