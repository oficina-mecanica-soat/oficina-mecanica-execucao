using System.Net;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Hosting;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.IntegrationTests;

public sealed class ProcessamentoRemotoWorkerTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    private const string Entrada = "https://sqs.us-east-1.amazonaws.com/123456789012/entrada-teste.fifo";
    private const string Resultados = "https://sqs.us-east-1.amazonaws.com/123456789012/resultados-teste.fifo";

    [Fact]
    public async Task Dado_FalhaPersistidaNaOutbox_Quando_RecriarWorkerECliente_Entao_PublicaMesmoResultadoAposBackoff()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var resultado = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados()));
        var primeira = (await cenario.Armazenamento.ObterPrimeiraOutboxAsync(default))!;
        var sqsAntes = SqsSemEntrada();
        sqsAntes.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Falha controlada antes do envio"));
        var clienteAnterior = fixture.CriarClient();
        using (clienteAnterior)
        await using (var provider = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<TimeProvider>(cenario.Relogio)
            .AddInfrastructureRemota(new(cenario.Tabela, Entrada, Resultados), clienteAnterior, sqsAntes.Object)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            var worker = Assert.IsType<ProcessamentoRemotoWorker>(Assert.Single(provider.GetServices<IHostedService>()));
            await worker.StartAsync(default);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                PendenciaOutbox? pendencia;
                do
                {
                    pendencia = (await cenario.Armazenamento.ObterPrimeiraOutboxAsync(timeout.Token))?.Ler<PendenciaOutbox>();
                    if (pendencia?.Tentativas != 1) await Task.Delay(25, timeout.Token);
                } while (pendencia?.Tentativas != 1);

                Assert.Equal("FALHA_PUBLICACAO", pendencia.UltimoErro);
                Assert.Equal(cenario.Relogio.GetUtcNow().AddSeconds(2), pendencia.ProximaTentativaEm);
                Assert.Null(pendencia.LeaseAte);
                Assert.Equal(resultado.MensagemId, pendencia.Mensagem.MensagemId);
            }
            finally { await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        }

        sqsAntes.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var sqsDepois = SqsSemEntrada();
        var enviado = new TaskCompletionSource<SendMessageRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        sqsDepois.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns((SendMessageRequest pedido, CancellationToken _) =>
            {
                enviado.TrySetResult(pedido);
                return Task.FromResult(new SendMessageResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "confirmado" });
            });
        using var clienteNovo = fixture.CriarClient();
        Assert.NotSame(clienteAnterior, clienteNovo);
        await using var novoProvider = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<TimeProvider>(cenario.Relogio)
            .AddInfrastructureRemota(new(cenario.Tabela, Entrada, Resultados), clienteNovo, sqsDepois.Object)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var novoWorker = Assert.IsType<ProcessamentoRemotoWorker>(Assert.Single(novoProvider.GetServices<IHostedService>()));
        cenario.Relogio.Avancar(TimeSpan.FromSeconds(3));
        await novoWorker.StartAsync(default);
        try
        {
            var pedido = await enviado.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Resultados, pedido.QueueUrl);
            Assert.Equal(resultado.MensagemId.ToString("D"), pedido.MessageDeduplicationId);
            Assert.Equal(resultado.OrdemServicoId.ToString("D"), pedido.MessageGroupId);
            Assert.Equal(JsonContrato.Serializar(resultado), pedido.MessageBody);
            await AguardarOutboxVaziaAsync(cenario);
            var enviada = await cenario.Armazenamento.ObterAsync(new("OUTBOX#ENVIADA", primeira.Chave.Ordenacao), default);
            Assert.Equal(resultado.MensagemId, enviada.Ler<PendenciaOutbox>()!.Mensagem.MensagemId);
            var saldo = (await cenario.SaldoAsync())!;
            Assert.Equal(7, saldo.Disponivel);
            Assert.Equal(3, saldo.Reservada);
        }
        finally { await novoWorker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10)); }

        Assert.True(novoWorker.ExecuteTask!.IsCompletedSuccessfully);
        sqsDepois.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        sqsAntes.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        sqsDepois.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        static Mock<IAmazonSQS> SqsSemEntrada()
        {
            var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
            sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ReceiveMessageRequest _, CancellationToken ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return new ReceiveMessageResponse();
                });
            return sqs;
        }
    }

    [Fact]
    public async Task Dado_OutboxPersistidaEPollBloqueado_Quando_IniciarWorker_Entao_PublicaEEncerraSemEsperarEntrada()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        var resultado = await cenario.EnviarAsync(cenario.Comando("PrepararExecucao", cenario.Dados() with { Itens = [] }));
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var pollIniciado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReceiveMessageRequest _, CancellationToken ct) =>
            {
                pollIniciado.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new ReceiveMessageResponse();
            });
        var enviado = new TaskCompletionSource<SendMessageRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        sqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns((SendMessageRequest pedido, CancellationToken _) =>
            {
                enviado.TrySetResult(pedido);
                return Task.FromResult(new SendMessageResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "confirmado" });
            });
        using var dynamo = fixture.CriarClient();
        await using var provider = new ServiceCollection().AddLogging().AddApplication()
            .AddInfrastructureRemota(new(cenario.Tabela, Entrada, Resultados), dynamo, sqs.Object).BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var worker = Assert.IsType<ProcessamentoRemotoWorker>(Assert.Single(provider.GetServices<IHostedService>()));
        await worker.StartAsync(default);
        try
        {
            await pollIniciado.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pedido = await enviado.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Resultados, pedido.QueueUrl);
            Assert.Equal(resultado.MensagemId.ToString("D"), pedido.MessageDeduplicationId);
            await AguardarOutboxVaziaAsync(cenario);
        }
        finally { await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10)); }

        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dado_FalhaDeConsumoEDeEnvio_Quando_Repetir_Entao_PreservaSaldoPublicaResultadoEConfirmaAposCommit()
    {
        using var cenario = await CenarioPreparacao.CriarAsync(fixture);
        await cenario.SemearAsync();
        var comando = cenario.Comando("PrepararExecucao", cenario.Dados());
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var recepcoes = 0;
        sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ReceiveMessageRequest _, CancellationToken ct) =>
            {
                switch (Interlocked.Increment(ref recepcoes))
                {
                    case 1: throw new IOException("Falha transitória de recepção");
                    case 2:
                        return new ReceiveMessageResponse
                        {
                            Messages = [new()
                        {
                            Body = JsonContrato.Serializar(comando), ReceiptHandle = "recibo",
                            Attributes = new() { ["MessageGroupId"] = comando.OrdemServicoId.ToString("D"),
                                ["MessageDeduplicationId"] = comando.MensagemId.ToString("D") }
                        }]
                        };
                    default:
                        await Task.Delay(Timeout.Infinite, ct);
                        return new ReceiveMessageResponse();
                }
            });
        var confirmado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var saldo = (await cenario.SaldoAsync())!;
                Assert.Equal(7, saldo.Disponivel);
                Assert.Equal(3, saldo.Reservada);
                confirmado.TrySetResult();
                return new DeleteMessageResponse();
            });
        var envios = new List<SendMessageRequest>();
        var enviado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns((SendMessageRequest pedido, CancellationToken _) =>
            {
                envios.Add(pedido);
                if (envios.Count == 1) throw new IOException("Falha transitória de envio");
                enviado.TrySetResult();
                return Task.FromResult(new SendMessageResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "confirmado" });
            });
        using var dynamo = fixture.CriarClient();
        await using var provider = new ServiceCollection().AddLogging().AddApplication()
            .AddInfrastructureRemota(new(cenario.Tabela, Entrada, Resultados), dynamo, sqs.Object).BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var worker = Assert.IsType<ProcessamentoRemotoWorker>(Assert.Single(provider.GetServices<IHostedService>()));
        await worker.StartAsync(default);
        try
        {
            await confirmado.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await enviado.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AguardarOutboxVaziaAsync(cenario);
        }
        finally { await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10)); }

        Assert.Equal(2, envios.Count);
        Assert.All(envios, p => Assert.Equal(Resultados, p.QueueUrl));
        Assert.Equal(envios[0].MessageBody, envios[1].MessageBody);
        Assert.Equal(envios[0].MessageDeduplicationId, envios[1].MessageDeduplicationId);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static async Task AguardarOutboxVaziaAsync(CenarioPreparacao cenario)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await cenario.Armazenamento.ObterPrimeiraOutboxAsync(timeout.Token) is not null)
            await Task.Delay(50, timeout.Token);
    }
}
