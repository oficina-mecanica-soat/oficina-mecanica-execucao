using OficinaMecanica.Execucao.Application.Mensagens;

namespace OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;

public sealed record CancelarPreparacaoRequest(Mensagem Origem, OficinaMecanica.Execucao.Application.Mensagens.CancelarPreparacao Dados);
