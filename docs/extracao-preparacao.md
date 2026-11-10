# F4-05 — primeiro incremento: regras locais de reserva e cancelamento

## Fontes verificadas

API em `4afed683e1e152bd24ddc16e7f5b0a003e26e432` (origin/develop após o PR #324). Os arquivos abaixo pertencem ao repositório `oficina-mecanica-api`; a extração não modifica o monólito.

| Fonte | Regra preservada neste incremento |
| --- | --- |
| `src/OficinaMecanica.Domain/GestaoEstoque/Entities/ItemEstoque.cs` | Quantidade positiva; disponível não negativo; reserva transfere disponível para reservado; estorno devolve; baixa consome reservado. Classe extraída para o Domain do Execução, sem dependência entre os projetos. |
| `tests/OficinaMecanica.Domain.UnitTests/GestaoEstoque/Entities/ItemEstoqueTests.cs` | Os 19 testes existentes acompanham a regra extraída. |
| `src/OficinaMecanica.Application/GestaoOrdemServico/OrdemServicoUseCases/AbrirOrdemServico/AbrirOrdemServicoValidator.cs` | Itens repetidos e quantidades inválidas são rejeitados. Solicitação sem peças é válida. |
| `src/OficinaMecanica.Application/GestaoOrdemServico/OrdemServicoUseCases/AbrirOrdemServico/AbrirOrdemServicoUseCase.cs` | Verificar disponibilidade de todos os itens antes de reservar. Sem saldo ou item ausente não há reserva parcial. |
| `src/OficinaMecanica.Application/GestaoOrdemServico/OrdemServicoUseCases/CancelarOrdemServico/CancelarOrdemServicoUseCase.cs` | Cancelamento devolve as quantidades reservadas. Falha de estoque impede confirmar a compensação. |
| `src/OficinaMecanica.Domain/GestaoOrdemServico/Aggregates/OrdemServico.cs` | Iniciar diagnóstico muda o estado; não produz laudo textual automaticamente. Estados públicos de OS continuam no serviço de OS. |
| ADR-0024/25/26 e RFC-0007 | Estoque físico no Execução; reserva atômica; compensação idempotente; nenhuma consulta ao banco de OS; falha de compensação não é sucesso. |

## Comportamento implementado

`PreparacaoEstoque` guarda IDs de preparação/OS/filial e um snapshot somente leitura dos itens. Recebe saldos da filial obtidos pelo chamador. Valida todo o lote antes de reservar ou liberar; um item inválido impede alterar os demais. Cancelar novamente a mesma instância retorna sem liberar duas vezes. Preparação sem peças pode ser cancelada sem movimentação.

Essa garantia é de domínio **em memória**. Persistência de preparação, saldos, deduplicação e outbox precisa ocorrer numa única transação DynamoDB com condições de concorrência. Esta classe não implementa confirmação de mensagem nem publicação. Não há consumer habilitado, endpoint novo, banco compartilhado ou alteração do Gateway. Não foi implementada execução física, nem cancelamento após seu início.

## Ponto de contrato a esclarecer antes dos handlers

A RFC-0007 contém, sim, o contrato de mensagens: envelope, dados mínimos, tipos e fluxo. Não é necessário reinventá-lo. A dúvida está na distinção entre duas versões:

| Trecho da RFC | Definição existente |
| --- | --- |
| Envelope | `versao = 1` para o contrato da mensagem. |
| Idempotência de negócio | Chave operação + OS + versão, além de `mensagemId`. |
| `PrepararExecucao` | `filialId`, `veiculoId`, `mecanicoId`, `itens`, `servicos`; não há versão explícita nos dados mínimos. |
| `ExecucaoPreparada` | Devolve `preparacaoId` e `versao`. |
| `CancelarPreparacao` | Já referencia `preparacaoId`; esse identificador pode localizar a versão persistida, sem acrescentar campo ao comando. |

Os documentos e o código versionado consultados não esclarecem se a versão da chave é a versão fixa do envelope ou a versão de negócio da preparação. Usar uma como a outra silenciosamente pode deduplicar indevidamente uma preparação posterior da mesma OS. Nenhum campo novo foi acrescentado à RFC ou implementado no protocolo neste incremento.

Se a intenção for versionar preparações da mesma OS, proposta para revisão conjunta: informar `dados.versao` em `PrepararExecucao`, diferenciá-la de `envelope.versao`, e devolver a mesma versão em `ExecucaoPreparada`. `CancelarPreparacao` pode permanecer como está, resolvendo versão por `preparacaoId`. A proposta não é uma decisão aprovada e não deve ser consumida pela outra frente antes de alinhamento.

## Continuação após esclarecimento

Implementar os use cases/handlers com o contrato acordado; gravar estado, saldos, deduplicação e outbox atomicamente; testar duplicatas com novo `mensagemId`, concorrência entre OS, resultado tardio e reinício. Adicionar DynamoDB Local e adaptador de mensagens para testes de processo. Manter AWS desligada e F4-04 pendente até a validação real pelas esteiras.

## Validação local em 10/11/2026

Build Release sem avisos/erros; formatação verificada; 49 testes aprovados, zero ignorados (38 de domínio e 11 da base API); cobertura consolidada por arquivo/linha de 98,62% (143/145 linhas). Inclui os testes de estoque que acompanharam a extração e novos cenários de lote, cancelamento repetido, falha antes de liberar e retomada da mesma instância após reposição do saldo ausente. Não é evidência de reinício de processo, concorrência DynamoDB, SQS ou recuperação persistente; essas verificações seguem pendentes.
