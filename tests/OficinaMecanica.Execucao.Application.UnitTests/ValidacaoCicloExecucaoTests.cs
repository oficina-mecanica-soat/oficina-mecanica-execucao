using Moq;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.IniciarExecucaoServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarServico;
using OficinaMecanica.Execucao.Application.Preparacoes.FinalizarExecucao;

namespace OficinaMecanica.Execucao.Application.UnitTests;

public sealed class ValidacaoCicloExecucaoTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Dado_IdVazio_Quando_AcionarCicloFisico_Entao_NaoAbreTransacao(int indice)
    {
        var unidade = new Mock<IUnidadeTrabalhoPreparacao>(MockBehavior.Strict);
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        ids[indice] = Guid.Empty;
        await Assert.ThrowsAsync<ContratoMensagemException>(() => new IniciarExecucaoServicoUseCase(unidade.Object,
            new IniciarExecucaoServicoValidator(), TimeProvider.System).ExecuteAsync(new(ids[0], ids[1], ids[2], ids[3])));
        await Assert.ThrowsAsync<ContratoMensagemException>(() => new FinalizarServicoUseCase(unidade.Object,
            new FinalizarServicoValidator(), TimeProvider.System).ExecuteAsync(new(ids[0], ids[1], ids[2], ids[3])));
        if (indice < 3)
            await Assert.ThrowsAsync<ContratoMensagemException>(() => new FinalizarExecucaoUseCase(unidade.Object,
                new FinalizarExecucaoValidator(), TimeProvider.System).ExecuteAsync(new(ids[0], ids[1], ids[2])));
        unidade.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Dado_VersaoOrcamentoInvalida_Quando_Autorizar_Entao_NaoAbreTransacao(int versao)
    {
        var unidade = new Mock<IUnidadeTrabalhoPreparacao>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ContratoMensagemException>(() => new AutorizarExecucaoUseCase(unidade.Object,
            new AutorizarExecucaoValidator()).ExecuteAsync(new(null!, new(Guid.NewGuid(), versao))));
        unidade.VerifyNoOtherCalls();
    }
}
