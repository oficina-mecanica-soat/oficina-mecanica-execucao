using System.Net;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;

namespace OficinaMecanica.Execucao.Infrastructure.UnitTests.Mensagens;

public sealed class AdaptadoresSqsTests
{
    private const string FilaEntrada = "https://sqs.us-east-1.amazonaws.com/123456789012/entrada-teste.fifo";
    private const string FilaOs = "https://sqs.us-east-1.amazonaws.com/123456789012/resultados-teste.fifo";

    [Fact]
    public async Task Dado_ResultadoPersistido_Quando_PublicarDuasVezes_Entao_PreservaEnvelopeGrupoEDeduplicacao()
    {
        var client = new Mock<IAmazonSQS>();
        var pedidos = new List<SendMessageRequest>();
        client.Setup(sqs => sqs.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((pedido, _) => pedidos.Add(pedido))
            .ReturnsAsync(new SendMessageResponse { MessageId = "confirmado", HttpStatusCode = HttpStatusCode.OK });
        var transporte = new TransporteSqs(client.Object, FilaOs);
        var mensagem = MensagemValida();

        await transporte.PublicarAsync(mensagem, default);
        await transporte.PublicarAsync(mensagem, default);

        pedidos.Should().HaveCount(2);
        pedidos.Should().OnlyContain(pedido => pedido.QueueUrl == FilaOs
            && pedido.MessageBody == JsonContrato.Serializar(mensagem)
            && pedido.MessageGroupId == mensagem.OrdemServicoId.ToString("D")
            && pedido.MessageDeduplicationId == mensagem.MensagemId.ToString("D"));
    }

    [Fact]
    public async Task Dado_RespostaIncertaDoSqs_Quando_Publicar_Entao_PropagaFalhaParaOutboxRepetir()
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(sqs => sqs.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Resposta perdida"));
        var executar = () => new TransporteSqs(client.Object, FilaOs).PublicarAsync(MensagemValida(), default);

        await executar.Should().ThrowAsync<IOException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://localhost:9324/fila.fifo")]
    [InlineData("https://sqs.us-east-1.amazonaws.com/123456789012/fila-standard")]
    public void Dado_FilaInvalida_Quando_CriarAdaptadores_Entao_RecusaConfiguracao(string url)
    {
        var sqs = new Mock<IAmazonSQS>();
        var processador = new Mock<IProcessadorMensagem>();
        Action publicar = () => _ = new TransporteSqs(sqs.Object, url);
        Action consumir = () => _ = new ConsumidorSqs(sqs.Object, processador.Object, url, TimeProvider.System);
        publicar.Should().Throw<ArgumentException>();
        consumir.Should().Throw<ArgumentException>();
        sqs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_PollSemMensagem_Quando_Consumir_Entao_NaoProcessaNemConfirma()
    {
        var c = new Cenario();
        c.Client.Setup(sqs => sqs.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ReceiveMessageResponse());

        (await c.Consumidor.ProcessarProximaAsync()).Should().BeFalse();

        c.Processador.VerifyNoOtherCalls();
        c.NaoConfirmou();
    }

    [Fact]
    public async Task Dado_CommitPendente_Quando_Consumir_Entao_ConfirmaSomenteDepoisDaConclusao()
    {
        var c = new Cenario();
        var commit = new TaskCompletionSource<Mensagem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Processador.Setup(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>())).Returns(commit.Task);

        var consumo = c.Consumidor.ProcessarProximaAsync();
        c.NaoConfirmou();
        commit.SetResult(c.Envelope);
        (await consumo.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        c.Client.Verify(sqs => sqs.ReceiveMessageAsync(It.Is<ReceiveMessageRequest>(pedido => pedido.QueueUrl == FilaEntrada && pedido.MaxNumberOfMessages == 1 && pedido.WaitTimeSeconds == 20 && pedido.VisibilityTimeout == 60), It.IsAny<CancellationToken>()), Times.Once);
        c.Client.Verify(sqs => sqs.DeleteMessageAsync(It.Is<DeleteMessageRequest>(pedido => pedido.QueueUrl == FilaEntrada && pedido.ReceiptHandle == "recibo-atual"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dado_FalhaDeContratoOuPersistencia_Quando_Processar_Entao_NaoConfirmaEntrada(bool contrato)
    {
        var c = new Cenario();
        Exception falha = contrato ? new ContratoMensagemException("TIPO_DESCONHECIDO") : new IOException("DynamoDB indisponível");
        c.Processador.Setup(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>())).ThrowsAsync(falha);
        var executar = () => c.Consumidor.ProcessarProximaAsync();

        await executar.Should().ThrowAsync<Exception>().Where(error => ReferenceEquals(error, falha));
        c.NaoConfirmou();
    }

    [Theory]
    [InlineData("MessageGroupId")]
    [InlineData("MessageDeduplicationId")]
    public async Task Dado_MetadadoFifoDivergente_Quando_Consumir_Entao_RejeitaSemProcessar(string atributo)
    {
        var c = new Cenario();
        c.Recebida.Attributes[atributo] = Guid.NewGuid().ToString("D");
        var executar = () => c.Consumidor.ProcessarProximaAsync();

        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage("METADADOS_FIFO_DIVERGENTES");
        c.Processador.VerifyNoOtherCalls();
        c.NaoConfirmou();
    }

    [Fact]
    public async Task Dado_ProcessamentoLongo_Quando_VisibilidadeRenovar_Entao_PreservaReciboEAguardaCommit()
    {
        var c = new Cenario();
        var commit = new TaskCompletionSource<Mensagem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var renovou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Processador.Setup(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>())).Returns(commit.Task);
        c.Client.Setup(sqs => sqs.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => renovou.TrySetResult()).ReturnsAsync(new ChangeMessageVisibilityResponse());

        var consumo = c.Consumidor.ProcessarProximaAsync();
        c.Relogio.Advance(TimeSpan.FromSeconds(20));
        await renovou.Task.WaitAsync(TimeSpan.FromSeconds(5));
        c.NaoConfirmou();
        commit.SetResult(c.Envelope);
        (await consumo.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        c.Client.Verify(sqs => sqs.ChangeMessageVisibilityAsync(It.Is<ChangeMessageVisibilityRequest>(pedido => pedido.QueueUrl == FilaEntrada && pedido.ReceiptHandle == "recibo-atual" && pedido.VisibilityTimeout == 60), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dado_FalhaNaRenovacao_Quando_Processar_Entao_CancelaTrabalhoENaoConfirma()
    {
        var c = new Cenario();
        c.Processador.Setup(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return c.Envelope; });
        c.Client.Setup(sqs => sqs.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Visibilidade incerta"));

        var consumo = c.Consumidor.ProcessarProximaAsync();
        c.Relogio.Advance(TimeSpan.FromSeconds(20));
        var executar = () => consumo.WaitAsync(TimeSpan.FromSeconds(5));

        await executar.Should().ThrowAsync<IOException>();
        c.NaoConfirmou();
    }

    [Fact]
    public async Task Dado_CancelamentoDuranteProcessamento_Quando_Encerrar_Entao_NaoConfirmaEntrada()
    {
        var c = new Cenario();
        using var cancelamento = new CancellationTokenSource();
        c.Processador.Setup(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return c.Envelope; });

        var consumo = c.Consumidor.ProcessarProximaAsync(cancelamento.Token);
        cancelamento.Cancel();
        var executar = () => consumo.WaitAsync(TimeSpan.FromSeconds(5));

        await executar.Should().ThrowAsync<OperationCanceledException>();
        c.NaoConfirmou();
    }

    [Fact]
    public async Task Dado_CommitConfirmadoEAckIncerto_Quando_ExcluirFalha_Entao_PropagaParaReentregaIdempotente()
    {
        var c = new Cenario();
        c.Client.Setup(sqs => sqs.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Ack incerto"));
        var executar = () => c.Consumidor.ProcessarProximaAsync();

        await executar.Should().ThrowAsync<IOException>();
        c.Processador.Verify(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(200, "")]
    [InlineData(500, "id-sem-confirmacao")]
    public async Task Dado_RespostaSemConfirmacao_Quando_Publicar_Entao_NaoTrataComoEnvioConcluido(int status, string id)
    {
        var client = new Mock<IAmazonSQS>();
        client.Setup(sqs => sqs.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { HttpStatusCode = (HttpStatusCode)status, MessageId = id });
        var executar = () => new TransporteSqs(client.Object, FilaOs).PublicarAsync(MensagemValida(), default);

        await executar.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dado_IdentificadorVazio_Quando_Publicar_Entao_NaoEnviaMensagemSemChaveDeIdempotencia(bool mensagemVazia)
    {
        var client = new Mock<IAmazonSQS>();
        var mensagem = MensagemValida();
        mensagem = mensagemVazia ? mensagem with { MensagemId = Guid.Empty } : mensagem with { OrdemServicoId = Guid.Empty };
        var executar = () => new TransporteSqs(client.Object, FilaOs).PublicarAsync(mensagem, default);

        await executar.Should().ThrowAsync<ContratoMensagemException>();
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dado_JsonMalformado_Quando_Consumir_Entao_PreservaEntradaParaRedrive()
    {
        var c = new Cenario();
        c.Recebida.Body = "{";
        var executar = () => c.Consumidor.ProcessarProximaAsync();

        await executar.Should().ThrowAsync<JsonException>();
        c.Processador.VerifyNoOtherCalls();
        c.NaoConfirmou();
    }

    [Fact]
    public async Task Dado_ReciboAusente_Quando_Consumir_Entao_NaoTentaExcluirPorOutroIdentificador()
    {
        var c = new Cenario();
        c.Recebida.ReceiptHandle = "";
        var executar = () => c.Consumidor.ProcessarProximaAsync();

        await executar.Should().ThrowAsync<ContratoMensagemException>().WithMessage("RECIBO_SQS_AUSENTE");
        c.Processador.VerifyNoOtherCalls();
        c.NaoConfirmou();
    }

    [Fact]
    public async Task Dado_IdsUuidComCaixaDiferente_Quando_Consumir_Entao_PreservaCompatibilidadeDoContrato()
    {
        var c = new Cenario();
        c.Recebida.Attributes["MessageGroupId"] = c.Envelope.OrdemServicoId.ToString("D").ToUpperInvariant();
        c.Recebida.Attributes["MessageDeduplicationId"] = c.Envelope.MensagemId.ToString("D").ToUpperInvariant();

        (await c.Consumidor.ProcessarProximaAsync()).Should().BeTrue();
        c.Processador.Verify(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mensagem MensagemValida() => new(Guid.NewGuid(), "ExecucaoPreparada", 1, Guid.NewGuid(), Guid.NewGuid(),
        "correlacao-de-teste", Guid.NewGuid(), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { versao = 1 }));

    [Fact]
    public async Task Dado_CarrierComCamposLivres_Quando_EnviarSqs_Entao_SerializaSomenteTraceparentSemAlterarDeduplicacao()
    {
        var client = new Mock<IAmazonSQS>();
        SendMessageRequest? enviada = null;
        client.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((pedido, _) => enviada = pedido)
            .ReturnsAsync(new SendMessageResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "confirmado" });
        var mensagem = MensagemValida() with
        {
            TraceContext = new()
            {
                ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                ["Authorization"] = "segredo-ficticio",
                ["baggage"] = "cpf=12345678901"
            }
        };
        await new TransporteSqs(client.Object, FilaOs).PublicarAsync(mensagem, default);
        Assert.Equal(mensagem.MensagemId.ToString("D"), enviada!.MessageDeduplicationId);
        Assert.Single(JsonContrato.Ler<Mensagem>(enviada.MessageBody).TraceContext!);
        Assert.DoesNotContain("segredo-ficticio", enviada.MessageBody);
        Assert.DoesNotContain("12345678901", enviada.MessageBody);
    }

    [Fact]
    public async Task Dado_ComandoSemEventoImediato_Quando_ConfirmarProcessamento_Entao_ConfirmaEntradaSomenteAposCommit()
    {
        var c = new Cenario();
        var commit = new TaskCompletionSource<Mensagem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Processador.Setup(p => p.ProcessarAsync(c.Corpo, It.IsAny<CancellationToken>())).Returns(commit.Task);
        var consumo = c.Consumidor.ProcessarProximaAsync();
        c.NaoConfirmou();
        commit.SetResult(null);
        Assert.True(await consumo);
        c.Client.Verify(sqs => sqs.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Cenario
    {
        public Mock<IAmazonSQS> Client { get; } = new();
        public Mock<IProcessadorMensagem> Processador { get; } = new();
        public FakeTimeProvider Relogio { get; } = new();
        public Mensagem Envelope { get; } = MensagemValida();
        public string Corpo => JsonContrato.Serializar(Envelope);
        public Amazon.SQS.Model.Message Recebida { get; }
        public ConsumidorSqs Consumidor { get; }
        public Cenario()
        {
            Recebida = new() { Body = Corpo, ReceiptHandle = "recibo-atual", Attributes = new() { ["MessageGroupId"] = Envelope.OrdemServicoId.ToString("D"), ["MessageDeduplicationId"] = Envelope.MensagemId.ToString("D") } };
            Client.Setup(sqs => sqs.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ReceiveMessageResponse { Messages = [Recebida] });
            Client.Setup(sqs => sqs.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DeleteMessageResponse());
            Processador.Setup(p => p.ProcessarAsync(Corpo, It.IsAny<CancellationToken>())).ReturnsAsync(Envelope);
            Consumidor = new(Client.Object, Processador.Object, FilaEntrada, Relogio);
        }
        public void NaoConfirmou() => Client.Verify(sqs => sqs.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
