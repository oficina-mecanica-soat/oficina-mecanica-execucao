# F4-05 — preparação e cancelamento persistentes

## Decisões aplicadas

Fontes: ADR-0024/25/26 e RFC-0007, incluindo o ajuste de versão aprovado no [PR #325](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/pull/325). `envelope.versao` permanece 1; `PrepararExecucao.dados.versao` identifica o snapshot gerenciado pela OS. Cancelamento resolve essa versão por `preparacaoId`. O serviço não consulta SQL da OS nem compartilha Domain/migrations.

## Regras extraídas

| Fonte no repositório da API | Comportamento preservado |
| --- | --- |
| `src/OficinaMecanica.Domain/GestaoEstoque/Entities/ItemEstoque.cs` e seus 19 testes | Reserva transfere disponível para reservado; estorno devolve; baixa consome reservado; quantidades são validadas. |
| `src/OficinaMecanica.Domain/GestaoEstoque/Aggregates/Estoque.cs` e seus 21 testes | Agregado dono dos itens; consulta disponibilidade, reserva e estorna por seus métodos existentes. Reconstituição local identifica o estoque pela filial; não compartilha o projeto Domain. |
| `AbrirOrdemServicoValidator` | Peças repetidas e quantidades inválidas são rejeitadas; solicitação sem peças é válida. |
| `AbrirOrdemServicoUseCase` | Verificar todos os saldos antes de reservar. Sem saldo/item não há reserva parcial. |
| `CancelarOrdemServicoUseCase` | Falha de estoque impede confirmar a compensação. |
| `OrdemServico.IniciarDiagnostico` | Não existe geração automática de laudo. O resultado desta preparação transporta o snapshot recebido de veículo, mecânico e serviços em `diagnostico`, sem inventar avaliação clínica/técnica. |

## Organização das camadas

- Domain: `Estoque`, `ItemEstoque`, `IEstoqueRepository` e regras de preparação/compensação. O agregado e seus testes foram extraídos da API; não há dependência entre os projetos Domain dos serviços.
- Application: `PrepararExecucao` e `CancelarPreparacao` têm Request, Response, Validator e UseCase próprios. O processador de mensagens valida o envelope e encaminha ao caso de uso. `IUnidadeTrabalhoPreparacao` expõe uma sessão tipada de preparação; não expõe tabela, chave física, revisão, JSON de armazenamento ou API DynamoDB. A Application coordena as regras e solicita o registro do resultado na mesma unidade de trabalho.
- Infrastructure: implementação da unidade de trabalho, repositório de estoque, documentos e chaves DynamoDB, condições/repetição por concorrência, gravação de deduplicação/outbox e publicação. `ProcessamentoLocalWorker` também pertence a esta camada e resolve os casos de uso por injeção de dependência.
- API: configuração HTTP/JWT/healthcheck e composição das dependências com `AddApplication`/`AddInfrastructure`.

Testes unitários próprios de Application verificam preparação, recusa, versões, reentrega, cancelamento e falhas de compensação com portas simuladas. Os testes de integração continuam comprovando a transação no DynamoDB Local; os testes unitários não substituem essa evidência.

## Transação e idempotência

O processador desserializa somente `PrepararExecucao` e `CancelarPreparacao` v1, valida os campos e grava estado, saldos, reservas, deduplicação e outbox via uma única transação DynamoDB. Leituras são consistentes e todas as revisões lidas entram como condições do commit, inclusive saldos ausentes numa recusa. Conflito provoca releitura; falha técnica não se transforma em recusa de estoque.

A operação é identificada por tipo + OS + versão de negócio; o hash compara os dados de negócio, com itens ordenados e valores decimal normalizados sem arredondamento. Metadados de transporte não mudam o snapshot. Reentrega devolve o resultado persistido, sem nova movimentação nem outra outbox. Reutilizar mensagemId para outra operação ou repetir a chave com payload diferente é conflito de contrato.

Uma nova preparação só é aceita com versão maior e sem preparação anterior ativa. Versão antiga/cancelada não reabre estoque. Cancelamento valida as reservas persistidas, resolve a versão pelo ID e libera saldos/estado atomicamente. ID ainda desconhecido mantém o comando pendente de recuperação, sem anunciar compensação. Repetir cancelamento antigo depois de preparar outra versão preserva a reserva atual.

## Modelo DynamoDB deste incremento

Uma tabela com PK/SK string, `revisao` numérica e `conteudo` JSON. Revisão técnica não é versão da preparação. A tabela real ainda será provisionada na F4-04; testes e Compose criam somente tabela em endpoint local.

| PK | SK | Acesso/ownership |
| --- | --- | --- |
| `ESTOQUE#{filialId}` | `ITEM#{itemId}` | Saldo por filial e item; ItemId corresponde à peça/insumo do catálogo. |
| `OS#{ordemServicoId}` | `ATUAL` | Versão atual e barreira para preparações concorrentes da mesma OS. |
| `OS#{ordemServicoId}` | `PREPARACAO#{versao}` | Snapshot e estado da preparação; histórico preservado. |
| `OS#{ordemServicoId}` | `RESERVA#{itemId}#{versao}` | Reserva por OS/item/versão, com marcador de liberação. |
| `PREPARACAO#{preparacaoId}` | `REFERENCIA` | Resolução direta de OS/versão no cancelamento. |
| `OS#{ordemServicoId}` | `OPERACAO#{tipo}#{versao}` | Hash e resultado imutável de cada operação. |
| `MENSAGEM#{mensagemId}` | `RECEBIDA` | Deduplicação do transporte, confirmada com o estado de negócio. |
| `OUTBOX#CONTROLE` | `SEQUENCIA` | Sequência transacional que preserva a ordem de commit dos resultados. |
| `OUTBOX#PENDENTE` | `{sequencia}#{mensagemId}` | Query ordenada, sem Scan; primeira pendência recebe lease antes do envio. |
| `OUTBOX#ENVIADA` | `{sequencia}#{mensagemId}` | Histórico após confirmação do transporte. |

Versões e sequência usam preenchimento numérico para ordenação lexicográfica. Limite técnico atual: **46 itens distintos por preparação**; são sete registros fixos mais saldo/reserva por item, totalizando até 99 ações. Lotes maiores são rejeitados para investigação, sem reserva parcial; não são divididos em transações que perderiam atomicidade. A [API DynamoDB limita a transação a 100 ações e 4 MB](https://docs.aws.amazon.com/amazondynamodb/latest/APIReference/API_TransactWriteItems.html).

## Outbox e transporte substituível

`PublicadorOutbox` é executado por BackgroundService no modo local. A sequência de commit impede inverter preparação/cancelamento mesmo no mesmo instante. Lease dura 60 segundos; disputa pela mesma pendência não publica em paralelo. Falha do transporte persiste tentativa, código sanitizado e backoff exponencial até 300 segundos. Após envio, falha antes da marcação permite reenviar a **mesma mensagem**, sem inventar exactly-once. A lease expira e outro processo pode retomar. O consumidor futuro precisa manter idempotência, como exige a RFC.

A publicação neste incremento é serializada globalmente; a primeira pendência em backoff bloqueia as seguintes. É uma escolha simples para preservar ordem no volume acadêmico, com gargalo e fila bloqueada visíveis nos registros. Paralelização por OS e alertas operacionais devem ser avaliados com medições na F4-11; não há alegação de throughput validado.

O worker local lê arquivos e só arquiva após commit. Mensagens malformadas/tipos desconhecidos vão para investigação local, sem processamento permissivo. Uma entrada temporariamente bloqueada não impede publicar outbox já confirmada. O transporte de saída grava arquivo de forma atômica por mensagemId. Esses adaptadores não implementam nem comprovam FIFO, DLQ, visibility timeout, IAM ou SQS real.

## Execução e validação

Consulte o README para Compose e execução da suíte de integração. O auxiliar de demonstração foi removido com a pasta de scripts; os testes de preparação/cancelamento e retomada persistente permanecem. O DynamoDB Local do Compose é em memória: reiniciar API preserva dados, encerrar DynamoDB os remove. A demonstração histórica verificou saldo 10→7/3→10/0 e dois reinícios reais; essa evidência não comprova permissões ou durabilidade AWS.

Os testes de integração criam DynamoDB Local temporário via Testcontainers, exigem Docker e nunca são ignorados. Cobrem concorrência por último saldo e pela mesma operação, versões sucessivas, compensação interrompida, falha atômica, commit antes do envio, envio antes da confirmação, leases, backoff e investigação de contratos inválidos. A dependência transitiva de testes SSH.NET foi fixada em [2026.0.0](https://github.com/sshnet/SSH.NET/releases/tag/2026.0.0), sem suprimir avisos de segurança do build.

## Limites da entrega

Evidência local da reorganização em 10/11/2026: restore/build Release e formato aprovados, zero avisos/erros; **147 testes aprovados, zero ignorados**, sendo 59 de domínio (incluindo os 21 testes extraídos de `Estoque`), 12 unitários de Application, 11 da base API e 65 de integração. Cobertura local consolidada por classe/linha: **96,62%** (629/651); o relatório consolidado do CI é a evidência do gate antes do merge. A demonstração histórica via Compose passou após a reorganização, comprovando estoque 10→7/3→10/0 e dois reinícios reais da API. Os dados de demonstração ficaram fora do Git; containers e rede foram encerrados ao final.

Este incremento conclui o fluxo local de preparação/cancelamento, não a F4-05 inteira. Autorizar execução, início/finalização física, métricas Datadog, consumidor/publicador SQS, tabelas/filas/SSM reais, integração da Saga e políticas HTTP permanecem nas tarefas seguintes. O worker por arquivos é opt-in e recusa ambiente diferente de Development. O workload AWS atual continua sem consumidor remoto habilitado. Nenhum apply/deploy foi executado, nem a AWS foi religada.
