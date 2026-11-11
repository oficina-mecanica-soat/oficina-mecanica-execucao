using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;

public sealed record AutorizarExecucaoRequest(Mensagem Origem, Mensagens.AutorizarExecucao Dados);
