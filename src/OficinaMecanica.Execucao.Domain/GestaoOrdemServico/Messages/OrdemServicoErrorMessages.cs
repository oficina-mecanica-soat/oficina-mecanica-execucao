namespace OficinaMecanica.Execucao.Domain.GestaoOrdemServico.Messages;

public static class OrdemServicoErrorMessages
{
    public const string ServicoCatalogoObrigatorio = "Serviço do catálogo é obrigatório.";
    public const string ValorServicoMaiorQueZero = "Valor do serviço deve ser maior que zero.";
    public const string ServicoPendenteParaIniciarExecucao = "Serviço deve estar pendente para iniciar execução.";
    public const string ServicoEmExecucaoParaFinalizar = "Serviço deve estar em execução para finalizar.";
}
