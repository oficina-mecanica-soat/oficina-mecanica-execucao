using System.Text.Json;
using FluentValidation;
using OficinaMecanica.Execucao.Application.Preparacoes;
using OficinaMecanica.Execucao.Application.Preparacoes.PrepararExecucao;
using OficinaMecanica.Execucao.Application.Preparacoes.CancelarPreparacao;
using OficinaMecanica.Execucao.Application.Preparacoes.AutorizarExecucao;

namespace OficinaMecanica.Execucao.Application.Mensagens;

public sealed record MensagemInspecionada(Mensagem Mensagem, string HashNegocio);

/// <summary>Valida o contrato e calcula o hash sem executar casos de uso ou acessar persistência.</summary>
public static class InspecaoMensagemPreparacao
{
    public static async Task<MensagemInspecionada> InspecionarAsync(string json, CancellationToken ct = default)
    {
        var mensagem = LerEnvelope(json);
        try
        {
            var hash = mensagem.Tipo switch
            {
                "PrepararExecucao" => await HashPreparacaoAsync(mensagem, ct),
                "CancelarPreparacao" => await HashCancelamentoAsync(mensagem, ct),
                "AutorizarExecucao" => await HashAutorizacaoAsync(mensagem, ct),
                _ => throw new ContratoMensagemException("TIPO_DESCONHECIDO")
            };
            return new(mensagem, hash);
        }
        catch (JsonException) { throw new ContratoMensagemException("DADOS_INVALIDOS"); }
    }

    internal static Mensagem LerEnvelope(string json)
    {
        try
        {
            var mensagem = JsonContrato.Ler<Mensagem>(json);
            if (mensagem.Versao != 1 || mensagem.MensagemId == Guid.Empty || mensagem.SagaId == Guid.Empty
                || mensagem.OrdemServicoId == Guid.Empty || mensagem.CausationId == Guid.Empty
                || string.IsNullOrWhiteSpace(mensagem.CorrelationId) || mensagem.OcorridoEm == default
                || mensagem.OcorridoEm.Offset != TimeSpan.Zero || mensagem.Dados.ValueKind != JsonValueKind.Object)
                throw new ContratoMensagemException("ENVELOPE_INVALIDO");
            return mensagem;
        }
        catch (JsonException) { throw new ContratoMensagemException("JSON_INVALIDO"); }
    }

    private static async Task<string> HashPreparacaoAsync(Mensagem mensagem, CancellationToken ct)
    {
        var dados = mensagem.Dados.Deserialize<PrepararExecucao>(JsonContrato.Options)
            ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS");
        await ValidarAsync(new PrepararExecucaoValidator(), new PrepararExecucaoRequest(mensagem, dados), ct);
        return ResultadosPreparacao.Hash(mensagem, ResultadosPreparacao.Normalizar(dados));
    }

    private static async Task<string> HashCancelamentoAsync(Mensagem mensagem, CancellationToken ct)
    {
        var dados = mensagem.Dados.Deserialize<CancelarPreparacao>(JsonContrato.Options)
            ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS");
        await ValidarAsync(new CancelarPreparacaoValidator(), new CancelarPreparacaoRequest(mensagem, dados), ct);
        return ResultadosPreparacao.Hash(mensagem, dados);
    }

    private static async Task<string> HashAutorizacaoAsync(Mensagem mensagem, CancellationToken ct)
    {
        var dados = mensagem.Dados.Deserialize<AutorizarExecucao>(JsonContrato.Options)
            ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS");
        await ValidarAsync(new AutorizarExecucaoValidator(), new AutorizarExecucaoRequest(mensagem, dados), ct);
        return ResultadosPreparacao.Hash(mensagem, dados);
    }

    private static async Task ValidarAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        var resultado = await validator.ValidateAsync(request, ct);
        if (!resultado.IsValid) throw new ContratoMensagemException(resultado.Errors[0].ErrorMessage);
    }
}
