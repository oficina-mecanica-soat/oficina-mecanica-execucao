using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;

public sealed class PrepararExecucaoValidator : AbstractValidator<PrepararExecucaoRequest>
{
    public PrepararExecucaoValidator()
    {
        RuleFor(request => request.Dados).Must(dados => dados is not null && !(dados.Versao <= 0 || dados.FilialId == Guid.Empty || dados.VeiculoId == Guid.Empty || dados.MecanicoId == Guid.Empty
            || dados.Itens is null || dados.Servicos is null || dados.Itens.Any(item => item is null || item.ItemId == Guid.Empty || item.Quantidade <= 0)
            || dados.Itens.Select(item => item.ItemId).Distinct().Count() != dados.Itens.Length
            || dados.Servicos.Any(servico => servico is null || servico.ServicoId == Guid.Empty || string.IsNullOrWhiteSpace(servico.Descricao) || servico.Valor <= 0))).WithMessage("PREPARACAO_INVALIDA");
    }
}
