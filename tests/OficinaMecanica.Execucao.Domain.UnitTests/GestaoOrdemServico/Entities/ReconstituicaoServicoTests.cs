using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Entities;
using OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Enums;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Domain.UnitTests.GestaoOrdemServico.Entities;

public sealed class ReconstituicaoServicoTests
{
    [Theory]
    [InlineData(StatusServico.Pendente)]
    [InlineData(StatusServico.EmExecucao)]
    [InlineData(StatusServico.Finalizado)]
    public void Dado_EstadoValido_Quando_Reconstituir_Entao_PreservaIdentidadesEstadoEDatas(StatusServico status)
    {
        var id = Guid.NewGuid();
        var catalogo = Guid.NewGuid();
        var inicio = status == StatusServico.Pendente ? (DateTime?)null : DateTime.UtcNow;
        var fim = status == StatusServico.Finalizado ? inicio!.Value.AddMinutes(1) : (DateTime?)null;
        var servico = Servico.Reconstituir(id, catalogo, 10m, status, inicio, fim);
        Assert.Equal(id, servico.Id);
        Assert.Equal(catalogo, servico.ServicoCatalogoId);
        Assert.Equal(status, servico.Status);
        Assert.Equal(inicio, servico.DataInicio);
        Assert.Equal(fim, servico.DataFim);
    }

    [Theory]
    [InlineData(StatusServico.Pendente, true, false)]
    [InlineData(StatusServico.EmExecucao, false, false)]
    [InlineData(StatusServico.EmExecucao, true, true)]
    [InlineData(StatusServico.Finalizado, true, false)]
    [InlineData((StatusServico)99, false, false)]
    public void Dado_EstadoInconsistente_Quando_Reconstituir_Entao_NaoInventaTransicao(StatusServico status, bool inicio, bool fim)
    {
        var agora = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => Servico.Reconstituir(Guid.NewGuid(), Guid.NewGuid(), 10m, status,
            inicio ? agora : null, fim ? agora.AddMinutes(1) : null));
    }

    [Fact]
    public void Dado_RelogioInvalido_Quando_IniciarOuFinalizar_Entao_PreservaEstado()
    {
        var servico = Servico.Criar(Guid.NewGuid(), 10m);
        Assert.Throws<DomainException>(() => servico.IniciarExecucao(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
        var inicio = DateTime.UtcNow;
        servico.IniciarExecucao(inicio);
        Assert.Throws<DomainException>(() => servico.Finalizar(inicio.AddSeconds(-1)));
        Assert.Equal(StatusServico.EmExecucao, servico.Status);
    }
}
