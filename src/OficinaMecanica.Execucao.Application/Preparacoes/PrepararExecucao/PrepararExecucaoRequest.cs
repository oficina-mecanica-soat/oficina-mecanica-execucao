using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;

public sealed record PrepararExecucaoRequest(Mensagem Origem, OficinaMecanica.Execucao.Application.Mensagens.PrepararExecucao Dados);
