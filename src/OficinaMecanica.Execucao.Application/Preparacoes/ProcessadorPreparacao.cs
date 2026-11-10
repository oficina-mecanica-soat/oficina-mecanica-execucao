using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OficinaMecanica.Execucao.Application.Mensagens;
using OficinaMecanica.Execucao.Application.Persistencia;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Entities;
using OficinaMecanica.Execucao.Domain.GestaoEstoque.Messages;
using OficinaMecanica.Execucao.Domain.Preparacoes;
using OficinaMecanica.Execucao.Domain.Shared.Exceptions;

namespace OficinaMecanica.Execucao.Application.Preparacoes;

public sealed class ProcessadorPreparacao(IArmazenamentoExecucao armazenamento, TimeProvider relogio)
{
    public async Task<Mensagem> ProcessarAsync(string json, CancellationToken cancellationToken = default)
    {
        Mensagem mensagem;
        try
        {
            mensagem = JsonContrato.Ler<Mensagem>(json);
            ValidarEnvelope(mensagem);
        }
        catch (JsonException) { throw new ContratoMensagemException("JSON_INVALIDO"); }

        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                return mensagem.Tipo switch
                {
                    "PrepararExecucao" => await PrepararAsync(mensagem, cancellationToken),
                    "CancelarPreparacao" => await CancelarAsync(mensagem, cancellationToken),
                    _ => throw new ContratoMensagemException("TIPO_DESCONHECIDO")
                };
            }
            catch (JsonException) { throw new ContratoMensagemException("DADOS_INVALIDOS"); }
            catch (ConflitoConcorrenciaException) when (tentativa < 7)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5 * (tentativa + 1)), cancellationToken);
            }
        }
    }

    private async Task<Mensagem> PrepararAsync(Mensagem mensagem, CancellationToken ct)
    {
        var dados = mensagem.Dados.Deserialize<PrepararExecucao>(JsonContrato.Options)
            ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS");
        ValidarPreparacao(dados);
        dados = dados with
        {
            Itens = dados.Itens.OrderBy(item => item.ItemId).ToArray(),
            Servicos = dados.Servicos.OrderBy(servico => servico.ServicoId)
                .Select(servico => servico with
                {
                    Valor = decimal.Parse(servico.Valor.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                }).ToArray()
        };
        var hash = Hash(mensagem, dados);
        var tx = new TransacaoExecucao(armazenamento);
        var operacao = ChavesExecucao.Operacao(mensagem.OrdemServicoId, dados.Versao, mensagem.Tipo);
        var anterior = await ObterOperacaoAsync(tx, mensagem, operacao, hash, ct);
        if (anterior is not null)
            return anterior;

        var atualKey = ChavesExecucao.Atual(mensagem.OrdemServicoId);
        var atual = await tx.LerAsync<PreparacaoAtual>(atualKey, ct);
        if ((atual is null && dados.Versao != 1) || (atual is not null && dados.Versao <= atual.Versao))
            throw new ContratoMensagemException("VERSAO_PREPARACAO_INVALIDA");
        if (atual?.Ativa == true)
            throw new ContratoMensagemException("PREPARACAO_ANTERIOR_ATIVA");

        var saldos = await ObterSaldosAsync(tx, dados.FilialId, dados.Itens, ct);
        var preparacaoId = Guid.NewGuid();
        var reservada = true;
        Mensagem resultado;
        try
        {
            PreparacaoEstoque.Reservar(preparacaoId, mensagem.OrdemServicoId, dados.FilialId,
                dados.Itens.Select(item => new ItemReserva(item.ItemId, item.Quantidade)), saldos);
            resultado = CriarResultado(mensagem, "ExecucaoPreparada", new ExecucaoPreparada(preparacaoId,
                dados.Versao, dados.Itens, new(dados.VeiculoId, dados.MecanicoId, dados.Servicos)));
        }
        catch (DomainException error) when (error.Message == EstoqueErrorMessages.EstoqueInsuficiente)
        {
            reservada = false;
            resultado = CriarResultado(mensagem, "PreparacaoRecusada", new PreparacaoRecusada(dados.Versao, "ESTOQUE_INSUFICIENTE"));
        }

        if (reservada)
        {
            SalvarSaldos(tx, dados.FilialId, saldos);
            foreach (var item in dados.Itens)
                await tx.AdicionarAsync(ChavesExecucao.Reserva(mensagem.OrdemServicoId, item.ItemId, dados.Versao),
                    new ReservaPersistida(preparacaoId, dados.FilialId, item.ItemId, item.Quantidade, false), ct);
        }
        tx.Salvar(atualKey, new PreparacaoAtual(dados.Versao, preparacaoId, reservada));
        await tx.AdicionarAsync(ChavesExecucao.Preparacao(mensagem.OrdemServicoId, dados.Versao),
            new EstadoPreparacao(preparacaoId, dados, reservada, false), ct);
        await tx.AdicionarAsync(ChavesExecucao.Referencia(preparacaoId), new ReferenciaPreparacao(mensagem.OrdemServicoId, dados.Versao), ct);
        await ConcluirAsync(tx, mensagem, operacao, hash, resultado, ct);
        return resultado;
    }

    private async Task<Mensagem> CancelarAsync(Mensagem mensagem, CancellationToken ct)
    {
        var dados = mensagem.Dados.Deserialize<CancelarPreparacao>(JsonContrato.Options)
            ?? throw new ContratoMensagemException("DADOS_OBRIGATORIOS");
        if (dados.PreparacaoId == Guid.Empty || string.IsNullOrWhiteSpace(dados.Motivo))
            throw new ContratoMensagemException("CANCELAMENTO_INVALIDO");
        var tx = new TransacaoExecucao(armazenamento);
        var referencia = await tx.LerAsync<ReferenciaPreparacao>(ChavesExecucao.Referencia(dados.PreparacaoId), ct)
            ?? throw new PreparacaoNaoEncontradaException();
        if (referencia.OrdemServicoId != mensagem.OrdemServicoId)
            throw new ContratoMensagemException("PREPARACAO_DE_OUTRA_OS");
        var hash = Hash(mensagem, dados);
        var operacao = ChavesExecucao.Operacao(mensagem.OrdemServicoId, referencia.Versao, mensagem.Tipo);
        var anterior = await ObterOperacaoAsync(tx, mensagem, operacao, hash, ct);
        if (anterior is not null)
            return anterior;

        var estadoKey = ChavesExecucao.Preparacao(mensagem.OrdemServicoId, referencia.Versao);
        var estado = await tx.LerAsync<EstadoPreparacao>(estadoKey, ct)
            ?? throw new PreparacaoNaoEncontradaException();
        var atualKey = ChavesExecucao.Atual(mensagem.OrdemServicoId);
        var atual = await tx.LerAsync<PreparacaoAtual>(atualKey, ct);
        if (estado.PreparacaoId != dados.PreparacaoId || atual?.PreparacaoId != dados.PreparacaoId || estado.Cancelada)
            throw new ContratoMensagemException("ESTADO_PREPARACAO_INCONSISTENTE");

        if (estado.Reservada)
        {
            foreach (var item in estado.Snapshot.Itens)
            {
                var reservaKey = ChavesExecucao.Reserva(mensagem.OrdemServicoId, item.ItemId, referencia.Versao);
                var reserva = await tx.LerAsync<ReservaPersistida>(reservaKey, ct);
                if (reserva is null || reserva.PreparacaoId != dados.PreparacaoId || reserva.Liberada
                    || reserva.Quantidade != item.Quantidade || reserva.FilialId != estado.Snapshot.FilialId)
                    throw new DomainException("Reserva persistida inconsistente; compensação não confirmada.");
                tx.Salvar(reservaKey, reserva with { Liberada = true });
            }
            var saldos = await ObterSaldosAsync(tx, estado.Snapshot.FilialId, estado.Snapshot.Itens, ct);
            var preparacao = PreparacaoEstoque.Reconstituir(dados.PreparacaoId, mensagem.OrdemServicoId,
                estado.Snapshot.FilialId, estado.Snapshot.Itens.Select(item => new ItemReserva(item.ItemId, item.Quantidade)), false);
            preparacao.Cancelar(saldos);
            SalvarSaldos(tx, estado.Snapshot.FilialId, saldos);
        }
        tx.Salvar(estadoKey, estado with { Cancelada = true });
        tx.Salvar(atualKey, atual with { Ativa = false });
        var resultado = CriarResultado(mensagem, "PreparacaoCancelada", new PreparacaoCancelada(dados.PreparacaoId, referencia.Versao));
        await ConcluirAsync(tx, mensagem, operacao, hash, resultado, ct);
        return resultado;
    }

    private static async Task<Mensagem?> ObterOperacaoAsync(TransacaoExecucao tx, Mensagem mensagem,
        ChaveRegistro operacao, string hash, CancellationToken ct)
    {
        var recebidaKey = ChavesExecucao.Recebida(mensagem.MensagemId);
        var recebida = await tx.LerAsync<MensagemRecebida>(recebidaKey, ct);
        if (recebida is not null && recebida.Hash != hash)
            throw new ContratoMensagemException("MENSAGEM_ID_REUTILIZADO");
        var existente = await tx.LerAsync<OperacaoProcessada>(operacao, ct);
        if (existente is null)
            return null;
        if (existente.Hash != hash)
            throw new ContratoMensagemException("SNAPSHOT_DIVERGENTE");
        tx.Salvar(recebidaKey, new MensagemRecebida(hash));
        await tx.ConfirmarAsync(ct);
        return existente.Resultado;
    }

    private static async Task<Dictionary<Guid, ItemEstoque>> ObterSaldosAsync(TransacaoExecucao tx,
        Guid filialId, ItemSolicitado[] itens, CancellationToken ct)
    {
        var saldos = new Dictionary<Guid, ItemEstoque>();
        foreach (var item in itens)
        {
            var saldo = await tx.LerAsync<SaldoEstoque>(ChavesExecucao.Estoque(filialId, item.ItemId), ct);
            if (saldo is not null)
                saldos.Add(item.ItemId, saldo.Reconstituir());
        }
        return saldos;
    }

    private static void SalvarSaldos(TransacaoExecucao tx, Guid filialId, Dictionary<Guid, ItemEstoque> saldos)
    {
        foreach (var (id, saldo) in saldos)
            tx.Salvar(ChavesExecucao.Estoque(filialId, id), SaldoEstoque.De(saldo));
    }

    private static async Task ConcluirAsync(TransacaoExecucao tx, Mensagem mensagem, ChaveRegistro operacao,
        string hash, Mensagem resultado, CancellationToken ct)
    {
        tx.Salvar(ChavesExecucao.Recebida(mensagem.MensagemId), new MensagemRecebida(hash));
        tx.Salvar(operacao, new OperacaoProcessada(hash, resultado));
        var sequencia = await tx.LerAsync<SequenciaOutbox>(ChavesExecucao.SequenciaOutbox, ct);
        var proxima = checked((sequencia?.Valor ?? 0) + 1);
        tx.Salvar(ChavesExecucao.SequenciaOutbox, new SequenciaOutbox(proxima));
        await tx.AdicionarAsync(ChavesExecucao.Outbox(proxima, resultado), new PendenciaOutbox(resultado), ct);
        await tx.ConfirmarAsync(ct);
    }

    private Mensagem CriarResultado<T>(Mensagem origem, string tipo, T dados) => new(Guid.CreateVersion7(), tipo, 1,
        origem.SagaId, origem.OrdemServicoId, origem.CorrelationId, origem.MensagemId, relogio.GetUtcNow(),
        JsonSerializer.SerializeToElement(dados, JsonContrato.Options));

    private static string Hash<T>(Mensagem mensagem, T dados) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonContrato.Serializar(new { mensagem.Tipo, mensagem.OrdemServicoId, Dados = dados }))));

    private static void ValidarEnvelope(Mensagem mensagem)
    {
        if (mensagem.Versao != 1 || mensagem.MensagemId == Guid.Empty || mensagem.SagaId == Guid.Empty
            || mensagem.OrdemServicoId == Guid.Empty || mensagem.CausationId == Guid.Empty
            || string.IsNullOrWhiteSpace(mensagem.CorrelationId) || mensagem.OcorridoEm == default
            || mensagem.OcorridoEm.Offset != TimeSpan.Zero || mensagem.Dados.ValueKind != JsonValueKind.Object)
            throw new ContratoMensagemException("ENVELOPE_INVALIDO");
    }

    private static void ValidarPreparacao(PrepararExecucao dados)
    {
        if (dados.Versao <= 0 || dados.FilialId == Guid.Empty || dados.VeiculoId == Guid.Empty || dados.MecanicoId == Guid.Empty
            || dados.Itens is null || dados.Servicos is null || dados.Itens.Any(item => item is null || item.ItemId == Guid.Empty || item.Quantidade <= 0)
            || dados.Itens.Select(item => item.ItemId).Distinct().Count() != dados.Itens.Length
            || dados.Servicos.Any(servico => servico is null || servico.ServicoId == Guid.Empty || string.IsNullOrWhiteSpace(servico.Descricao) || servico.Valor <= 0))
            throw new ContratoMensagemException("PREPARACAO_INVALIDA");
        // Sete registros fixos, saldo e reserva por item devem caber nas 100 ações.
        if (dados.Itens.Length > 46)
            throw new ContratoMensagemException("LIMITE_TRANSACAO_DYNAMODB");
    }
}
