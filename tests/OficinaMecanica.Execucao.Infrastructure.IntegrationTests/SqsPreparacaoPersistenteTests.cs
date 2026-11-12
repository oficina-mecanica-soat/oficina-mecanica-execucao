using System.Net;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Moq;
using Microsoft.Extensions.Time.Testing;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class SqsPreparacaoPersistenteTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    private const string Entrada = "https://sqs.us-east-1.amazonaws.com/123456789012/entrada-teste.fifo";
    private const string Resultados = "https://sqs.us-east-1.amazonaws.com/123456789012/resultados-teste.fifo";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dado_CommitBloqueado_Quando_RenovarVisibilidade_Entao_AckESaldoDependemDoCommit(bool falharRenovacao)
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var armazenamento = new ArmazenamentoCommitControlado(cenario.Armazenamento);
        var processador = CenarioPreparacao.CriarProcessador(armazenamento, cenario.Relogio);
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados());
        ConfigurarRecepcao(sqs, comando);
        var renovou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sqs.Setup(s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChangeMessageVisibilityRequest pedido, CancellationToken _) =>
            {
                Assert.Equal(Entrada, pedido.QueueUrl);
                Assert.Equal("recibo", pedido.ReceiptHandle);
                Assert.Equal(60, pedido.VisibilityTimeout);
                renovou.TrySetResult();
                return falharRenovacao
                    ? Task.FromException<ChangeMessageVisibilityResponse>(new IOException("Renovação incerta"))
                    : Task.FromResult(new ChangeMessageVisibilityResponse { HttpStatusCode = HttpStatusCode.OK });
            });
        sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var saldo = (await cenario.SaldoAsync())!;
                Assert.Equal(7, saldo.Disponivel);
                Assert.Equal(3, saldo.Reservada);
                Assert.NotNull(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
                return new DeleteMessageResponse { HttpStatusCode = HttpStatusCode.OK };
            });
        var relogio = new FakeTimeProvider();
        var consumidor = new ConsumidorSqs(sqs.Object, processador, Entrada, relogio);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var consumo = consumidor.ProcessarProximaAsync(limite.Token);
        try
        {
            await armazenamento.CommitIniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
            Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
            relogio.Advance(TimeSpan.FromSeconds(20));
            await renovou.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);

            if (falharRenovacao)
            {
                await Assert.ThrowsAsync<IOException>(() => consumo.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(10, (await cenario.SaldoAsync())!.Disponivel);
                Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
                Assert.Null((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Atual(cenario.OsId), default)).Json);
                Assert.Null((await cenario.Armazenamento.ObterAsync(ChavesExecucao.Recebida(comando.MensagemId), default)).Json);
                sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            else
            {
                armazenamento.LiberarCommit.TrySetResult();
                Assert.True(await consumo.WaitAsync(TimeSpan.FromSeconds(5)));
                sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            }

            relogio.Advance(TimeSpan.FromMinutes(2));
            sqs.Verify(s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            limite.Cancel();
            armazenamento.LiberarCommit.TrySetResult();
            try { await consumo; } catch (OperationCanceledException) { } catch (IOException) { }
        }
    }

    private sealed class ArmazenamentoCommitControlado(IArmazenamentoExecucao original) : IArmazenamentoExecucao
    {
        public TaskCompletionSource CommitIniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LiberarCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Registro> ObterAsync(ChaveRegistro chave, CancellationToken ct) => original.ObterAsync(chave, ct);
        public Task<Registro?> ObterPrimeiraOutboxAsync(CancellationToken ct) => original.ObterPrimeiraOutboxAsync(ct);
        public async Task ConfirmarAsync(IReadOnlyCollection<AlteracaoRegistro> registros, CancellationToken ct)
        {
            CommitIniciado.TrySetResult();
            await LiberarCommit.Task.WaitAsync(ct);
            await original.ConfirmarAsync(registros, ct);
        }
    }

    [Fact]
    public async Task Dado_ComandoSqs_Quando_PrepararReentregarECancelar_Entao_AckSegueCommitELiberaUmaVez()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var client = new Mock<IAmazonSQS>();
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados());
        var consumidor = new ConsumidorSqs(client.Object, cenario.Processador, Entrada, cenario.Relogio);
        var enviados = new List<SendMessageRequest>();
        ConfigurarRecepcao(client, comando);
        client.Setup(sqs => sqs.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((pedido, _) => enviados.Add(pedido))
            .ReturnsAsync(new SendMessageResponse { MessageId = "sqs-confirmou", HttpStatusCode = HttpStatusCode.OK });
        var confirmacoes = 0;
        client.Setup(sqs => sqs.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                confirmacoes++;
                var saldo = (await cenario.SaldoAsync())!;
                Assert.Equal(confirmacoes <= 2 ? 7 : 10, saldo.Disponivel);
                Assert.Equal(confirmacoes <= 2 ? 3 : 0, saldo.Reservada);
                if (confirmacoes is 1 or 3) Assert.NotNull(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
                return new DeleteMessageResponse();
            });

        Assert.True(await consumidor.ProcessarProximaAsync());
        var preparada = (await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default))!.Ler<PendenciaOutbox>()!.Mensagem;
        var publicador = new PublicadorOutbox(cenario.Armazenamento, new TransporteSqs(client.Object, Resultados), cenario.Relogio);
        Assert.True(await publicador.PublicarProximaAsync());
        Assert.Equal(preparada.MensagemId.ToString("D"), Assert.Single(enviados).MessageDeduplicationId);

        Assert.True(await consumidor.ProcessarProximaAsync());
        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
        var preparacaoId = preparada.Dados.Deserialize<ExecucaoPreparada>(JsonContrato.Options)!.PreparacaoId;
        ConfigurarRecepcao(client, cenario.Comando("CancelarPreparacao", new CancelarPreparacao(preparacaoId, "Orçamento recusado")));
        Assert.True(await consumidor.ProcessarProximaAsync());
        Assert.True(await publicador.PublicarProximaAsync());
        Assert.True(await consumidor.ProcessarProximaAsync());

        Assert.Equal(4, confirmacoes);
        Assert.Equal(2, enviados.Count);
        Assert.Equal(["ExecucaoPreparada", "PreparacaoCancelada"], enviados.Select(p => JsonContrato.Ler<Mensagem>(p.MessageBody).Tipo).ToArray());
        Assert.All(enviados, pedido => Assert.Equal(Resultados, pedido.QueueUrl));
        Assert.Null(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
    }

    [Fact]
    public async Task Dado_EnvioConfirmadoEGravacaoInterrompida_Quando_RetomarOutbox_Entao_UsaMesmaDeduplicacaoSqs()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        var client = new Mock<IAmazonSQS>();
        var enviados = new List<SendMessageRequest>();
        client.Setup(sqs => sqs.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((pedido, _) => enviados.Add(pedido))
            .ReturnsAsync(new SendMessageResponse { MessageId = "sqs-confirmou", HttpStatusCode = HttpStatusCode.OK });
        var armazenamento = new ArmazenamentoComFalha(cenario.Armazenamento) { FalharConfirmacaoNumero = 2 };
        var transporte = new TransporteSqs(client.Object, Resultados);

        await Assert.ThrowsAsync<IOException>(() => new PublicadorOutbox(armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());
        Assert.NotNull(await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default));
        cenario.Relogio.Avancar(TimeSpan.FromMinutes(2));
        Assert.True(await new PublicadorOutbox(cenario.Armazenamento, transporte, cenario.Relogio).PublicarProximaAsync());

        Assert.Equal(2, enviados.Count);
        Assert.Equal(enviados[0].MessageDeduplicationId, enviados[1].MessageDeduplicationId);
        Assert.Equal(enviados[0].MessageGroupId, enviados[1].MessageGroupId);
        Assert.Equal(enviados[0].MessageBody, enviados[1].MessageBody);
    }

    private static void ConfigurarRecepcao(Mock<IAmazonSQS> client, Mensagem comando)
    {
        client.Setup(sqs => sqs.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = [new() { Body = JsonContrato.Serializar(comando), ReceiptHandle = "recibo", Attributes = new()
                { ["MessageGroupId"] = comando.OrdemServicoId.ToString("D"), ["MessageDeduplicationId"] = comando.MensagemId.ToString("D") } }]
            });
    }
}
