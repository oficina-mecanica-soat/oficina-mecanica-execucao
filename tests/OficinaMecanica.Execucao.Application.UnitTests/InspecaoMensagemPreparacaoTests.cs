using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using FluentAssertions;
using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.UnitTests;

public sealed class InspecaoMensagemPreparacaoTests
{
    // Envelopes sintéticos produzidos pelo ProcessadorResultadoOrcamento da OS (92f5d1e),
    // após resultados reais de orçamento (develop 7b28ae8). Não serializar com o DTO consumidor:
    // estes snapshots verificam a fronteira JSON sem dependência entre repositórios no CI.
    private const string AutorizacaoDaSaga = """
        {
          "mensagemId": "01a1d71c-5f0c-7aad-a5a5-7d2b861894e3",
          "tipo": "AutorizarExecucao",
          "versao": 1,
          "sagaId": "6a9eecc2-18ca-463f-9746-9f6375784978",
          "ordemServicoId": "f0f89ae6-2cf1-412f-8c1d-abb01eb3c273",
          "correlationId": "validacao-local-f4-10",
          "causationId": "01a1d71c-5f08-7635-83fa-e14c608eaf5f",
          "ocorridoEm": "2026-11-13T22:52:03.2127349+00:00",
          "dados": {
            "preparacaoId": "acc68c97-ef1d-4333-9222-07f8a3a67cc9",
            "versaoOrcamento": 3
          },
          "traceContext": {
            "traceparent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
          }
        }
        """;

    private const string CancelamentoDaSaga = """
        {
          "mensagemId": "01a1d71c-5f65-738c-a223-e6a5ee4ddc5f",
          "tipo": "CancelarPreparacao",
          "versao": 1,
          "sagaId": "8460cd10-e27b-4b70-a6be-49725ff63130",
          "ordemServicoId": "a811491d-505b-4040-bccb-c0187b7550c0",
          "correlationId": "validacao-local-f4-10",
          "causationId": "01a1d71c-5f65-7249-88aa-6b0c37264e16",
          "ocorridoEm": "2026-11-13T22:52:03.3017234+00:00",
          "dados": {
            "preparacaoId": "2c404f41-3ca0-4c78-af24-dc3101402469",
            "motivo": "Orçamento recusado"
          },
          "traceContext": {
            "traceparent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
          }
        }
        """;

    [Theory]
    [InlineData(AutorizacaoDaSaga, "AutorizarExecucao", "7653B7C12B642112D655753FD963A5938684F47BF76CFE828228D10E88E0C9EA")]
    [InlineData(CancelamentoDaSaga, "CancelarPreparacao", "8EF296AF2ACD35CACD825E2CFE415E8A0579D98838C7AC57019049EA01FE83AB")]
    public async Task Dado_EnvelopeRealDoProdutorOs_Quando_Inspecionar_Entao_PreservaContratoEIdentidade(string json, string tipo, string hash)
    {
        var inspecao = await InspecaoMensagemPreparacao.InspecionarAsync(json);
        var mensagem = inspecao.Mensagem;
        var reentrega = mensagem with { MensagemId = Guid.NewGuid() };

        var repetida = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(reentrega));

        mensagem.Tipo.Should().Be(tipo);
        mensagem.CorrelationId.Should().Be("validacao-local-f4-10");
        mensagem.TraceContext!["traceparent"].Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        inspecao.HashNegocio.Should().Be(hash);
        repetida.HashNegocio.Should().Be(hash);
        if (tipo == "AutorizarExecucao")
        {
            // A preparação usada pelo produtor era versão 2; o orçamento aprovado é versão 3.
            var dados = mensagem.Dados.Deserialize<AutorizarExecucao>(JsonContrato.Options)!;
            dados.VersaoOrcamento.Should().Be(3);
            dados.PreparacaoId.Should().Be(Guid.Parse("acc68c97-ef1d-4333-9222-07f8a3a67cc9"));
        }
        else
        {
            mensagem.Dados.Deserialize<CancelarPreparacao>(JsonContrato.Options)!.Motivo.Should().Be("Orçamento recusado");
        }
    }

    [Fact]
    public async Task Dado_EnvelopeDoProdutorComConteudoAlterado_Quando_Inspecionar_Entao_NaoConfundeComAReentrega()
    {
        var original = await InspecaoMensagemPreparacao.InspecionarAsync(AutorizacaoDaSaga);
        var alterada = JsonNode.Parse(AutorizacaoDaSaga)!;
        alterada["dados"]!["versaoOrcamento"] = 4;

        var inspecao = await InspecaoMensagemPreparacao.InspecionarAsync(alterada.ToJsonString());

        inspecao.Mensagem.MensagemId.Should().Be(original.Mensagem.MensagemId);
        inspecao.HashNegocio.Should().NotBe(original.HashNegocio);
    }

    [Fact]
    public async Task Dado_ValorUsualComEscala_Quando_Inspecionar_Entao_PreservaHashDoContratoExistente()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dados = new PrepararExecucao(1, id, id, id, [],
            [new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Teste", 125.500m,
                Guid.Parse("33333333-3333-3333-3333-333333333333"))]);
        var mensagem = Criar("PrepararExecucao") with
        {
            OrdemServicoId = id,
            Dados = JsonSerializer.SerializeToElement(dados, JsonContrato.Options)
        };

        var inspecao = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(mensagem));

        inspecao.HashNegocio.Should().Be("913BB997496AF87A6015500B0F5363EA8651E74617864F69775A8079539A51C9");
    }

    [Theory]
    [InlineData("0.00001", "0.0000100")]
    [InlineData("1E-28", "0.0000000000000000000000000001")]
    [InlineData("125.50", "125.5000")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    public async Task Dado_DecimalValido_Quando_Normalizar_Entao_PreservaPrecisaoEHashEntreEscalas(string texto, string outraEscala)
    {
        var valor = decimal.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture);
        var equivalente = decimal.Parse(outraEscala, NumberStyles.Float, CultureInfo.InvariantCulture);
        var mensagem = Criar("PrepararExecucao");
        var dados = mensagem.Dados.Deserialize<PrepararExecucao>(JsonContrato.Options)!;
        Mensagem ComValor(decimal numero) => mensagem with
        {
            Dados = JsonSerializer.SerializeToElement(dados with
            {
                Servicos = dados.Servicos.Select(s => s with { Valor = numero }).ToArray()
            }, JsonContrato.Options)
        };

        var original = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(ComValor(valor)));
        var reentrega = await InspecaoMensagemPreparacao.InspecionarAsync(JsonContrato.Serializar(ComValor(equivalente)));

        original.HashNegocio.Should().Be(reentrega.HashNegocio);
        original.Mensagem.Dados.GetProperty("servicos")[0].GetProperty("valor").GetDecimal().Should().Be(valor);
        reentrega.Mensagem.Dados.GetProperty("servicos")[0].GetProperty("valor").GetDecimal().Should().Be(valor);
    }

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
