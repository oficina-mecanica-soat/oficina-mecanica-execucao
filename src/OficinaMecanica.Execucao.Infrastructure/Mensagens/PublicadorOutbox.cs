using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Infrastructure.Persistencia;

namespace OficinaMecanica.Execucao.Infrastructure.Mensagens;

public sealed class PublicadorOutbox(IArmazenamentoExecucao armazenamento, ITransporteResultado transporte, TimeProvider relogio)
{
    public async Task<bool> PublicarProximaAsync(CancellationToken ct = default)
    {
        var registro = await armazenamento.ObterPrimeiraOutboxAsync(ct);
        if (registro is null)
            return false;
        var pendencia = registro.Ler<PendenciaOutbox>()!;
        var agora = relogio.GetUtcNow();
        if (pendencia.LeaseAte > agora || pendencia.ProximaTentativaEm > agora)
            return false;
        var lease = pendencia with { Responsavel = Guid.NewGuid().ToString("N"), LeaseAte = agora.AddMinutes(1) };
        try
        {
            await armazenamento.ConfirmarAsync([new(registro, true, JsonContrato.Serializar(lease))], ct);
        }
        catch (ConflitoConcorrenciaException) { return false; }

        // Após o transporte confirmar, falha de persistência deve manter a mesma mensagem
        // para reenvio após expirar a lease, nunca gerar outro mensagemId.
        try
        {
            await transporte.PublicarAsync(lease.Mensagem, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            var tentativas = lease.Tentativas + 1;
            var falha = lease with
            {
                Tentativas = tentativas,
                Responsavel = null,
                LeaseAte = null,
                ProximaTentativaEm = relogio.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(tentativas, 8)))),
                UltimoErro = "FALHA_PUBLICACAO"
            };
            await armazenamento.ConfirmarAsync([new(new(registro.Chave, registro.Revisao + 1,
                JsonContrato.Serializar(lease)), true, JsonContrato.Serializar(falha))], ct);
            return false;
        }

        var enviadaKey = new ChaveRegistro("OUTBOX#ENVIADA", registro.Chave.Ordenacao);
        var enviada = await armazenamento.ObterAsync(enviadaKey, ct);
        await armazenamento.ConfirmarAsync([
            new(new(registro.Chave, registro.Revisao + 1, JsonContrato.Serializar(lease)), true, null),
            new(enviada, true, JsonContrato.Serializar(lease with { Responsavel = null, LeaseAte = null }))
        ], ct);
        return true;
    }
}
