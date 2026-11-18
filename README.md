# 🔧 Oficina Mecânica — Execução

Microsserviço responsável por preparação, reserva/baixa de estoque e trabalho físico do mecânico. OS coordena a Saga; Orçamento/Pagamento mantém orçamento e cobrança. Os dados de Execução pertencem ao DynamoDB, sem consultas ao banco dos demais serviços.

## 🏗️ Recursos provisionados por esta esteira

| Recurso próprio | Uso e integração |
| --- | --- |
| Namespace, Deployment, Secret e Service NodePort 30081 | Workload de Execução; imagem publicada no ECR de Execução criado pela plataforma |
| Tabela DynamoDB | Estado de preparação/execução, estoque, reservas, deduplicação e Outbox; recursos definidos em `dados-mensageria.tf` |
| Entrada SQS FIFO, DLQ e política de redrive | Comandos da Saga de OS; os resultados são publicados na fila de OS, que pertence à API |
| Parâmetros SSM | Nome/ARN da tabela, URLs/ARNs das filas e prontidão de infraestrutura |

Consome EKS, ECR e listener privado 8081 da esteira Kubernetes e a fila de resultados da API. Não cria banco SQL no RDS, outro cluster ou NLB. O Terraform atual não cria ConfigMap nem HPA para Execução: configurações não secretas são definidas no Deployment. Fonte: [`infra/terraform/environments/dev`](infra/terraform/environments/dev).

<a id="arquitetura"></a>

## 🏗️ Arquitetura

Clean Architecture com Domain, Application, Infrastructure e API. A autorização da Saga é persistida antes das ações manuais. Preparar ou aprovar orçamento não inicia o reparo. O primeiro início publica `ExecucaoIniciada`; a finalização exige todos os serviços concluídos e confirma baixa de estoque, estado e Outbox na mesma transação. Após início físico, falhas técnicas exigem recuperação, sem compensação fictícia do reparo.

As mensagens usam SQS FIFO por OS, Inbox/Outbox e IDs estáveis. `servicoId` identifica a instância na OS; `servicoCatalogoId` identifica o catálogo. Repetir uma operação compatível não repete a baixa.


<a id="http--ações-do-mecânico"></a>

## 🔧 HTTP — ações do mecânico

Contrato definido na RFC-0007. A base é `/api/v1/execucoes/{ordemServicoId}/preparacoes/{preparacaoId}`. Os sufixos e perfis derivam de `OrdensServicoController` da API; as mutações físicas pertencem a este serviço no fluxo distribuído.

| Método e sufixo | Uso | Resposta de sucesso |
| --- | --- | --- |
| GET na base | Consultar estado local, inclusive antes de autorizar ou após cancelar | 200 |
| POST `/servicos/{servicoId}/iniciar-execucao` | Iniciar manualmente um serviço autorizado | 202 |
| POST `/servicos/{servicoId}/finalizar` | Finalizar um serviço individual | 202 |
| POST `/finalizar` | Finalizar explicitamente a execução e confirmar a baixa | 202 |

Todos exigem JWT válido com role `Administrador` ou `Mecanico`, como na referência. Cliente, Atendente e outros perfis recebem 403; token ausente/inválido recebe 401. Não há regra nova de vínculo usuário/mecânico: preserva a autorização por perfil atual. Não foi ampliada a lista de emissores/audiences nem alterada a chave configurada. A Lambda continua emitindo JWT de Cliente; isso não autoriza reparos. API, Auth Lambda e participantes utilizam o contrato JWT compartilhado do ambiente.

O corpo dos POSTs contém somente `{ "operacaoId": "UUID" }`. Use um UUID não vazio por ação e preserve-o nas repetições; use IDs distintos para iniciar, finalizar serviço e finalizar execução. Os IDs de domínio vêm da rota. Corpo extra, malformado ou inválido retorna 400. A resposta 202 traz `operacaoId`, `resultado` e `Location` para o GET do recurso, somente após commit local. Não significa que a OS recebeu o evento, que houve pagamento ou que a Saga concluiu. Não foi criada rota HTTP de `AutorizarExecucao`.

O GET projeta preparação, versões, indicadores históricos de reserva/cancelamento e ciclo de serviços; não expõe envelope, hash ou credenciais. `reservada` significa que a reserva foi confirmada historicamente, não o saldo atual. Uma preparação cancelada continua consultável, inclusive após preparar outra versão. Não grava estado de negócio; usa leituras consistentes com condições na unidade de trabalho existente.

Erros seguem `ErrorResponse`/`TipoErro` extraídos da API: 404 para preparação ausente ou de outra OS; 409 para conflito de operação/persistência; 422 para regra de negócio; 500 genérico para falha técnica. Não retorna detalhes sensíveis do SDK. No host atual, autenticação/perfil são conferidos antes da persistência e um modo de processamento compatível precisa estar habilitado. Caso contrário, identidade autorizada recebe 503 antes de resolver clientes. Não há fallback de processamento remoto para armazenamento local; health HTTP não substitui a validação dos recursos. `/api/health` permanece público.

<a id="estoque"></a>

## 📦 Estoque

`POST /api/v1/execucoes/estoque/registrar-entrada` aceita `operacaoId`, `filialId`, `pecaInsumoCatalogoId` e `quantidade` positiva para Administrador/Atendente. A entrada é idempotente: repetir o ID e conteúdo retorna o resultado anterior; conteúdo divergente retorna conflito. Abastecer este serviço, não o estoque histórico da API.

<a id="execução-local"></a>

## 🚀 Execução local

Pré-requisitos: .NET SDK 10 e Docker Desktop. O Compose sobe API em 5094 e DynamoDB Local em `127.0.0.1:8000`, sem AWS ou SQL Server. DynamoDB Local usa credenciais fictícias definidas no cliente local, sem consultar credenciais do laboratório. Os dados do DynamoDB desta configuração são voláteis e permanecem enquanto seu container estiver ativo; reiniciar apenas a API preserva o estado, mas `compose down` o remove.

```powershell
Copy-Item .env.example .env
# Preencha JWT_SECRET com uma chave local de pelo menos 32 bytes.
docker compose --env-file .env up -d --build
curl.exe --fail http://localhost:5094/api/health
docker compose down
```

Resposta esperada: `Healthy`. A porta 5094 evita conflito com a API atual em 5093. O processo recusa inicialização sem issuer, audience e chave válida. O healthcheck é público e não comprova persistência. As rotas manuais e suas condições de disponibilidade estão descritas na seção HTTP local.

<a id="testes-e-qualidade"></a>

## 🧪 Testes e qualidade

```powershell
dotnet restore OficinaMecanica.Execucao.sln
dotnet build OficinaMecanica.Execucao.sln -c Release --no-restore -warnaserror
dotnet format OficinaMecanica.Execucao.sln --verify-no-changes --no-restore
dotnet test OficinaMecanica.Execucao.sln -c Release --no-build --collect:"XPlat Code Coverage" --settings tests/sonarqube.runsettings --results-directory TestResults --logger trx
```

A CI segue a referência da API: Git Flow, build Release com analisadores e avisos como erros, formato, testes sem ignorados, cobertura mínima de **90%**, Docker, manifests Kubernetes e Terraform. O mínimo acadêmico de 80% não reduz o gate do projeto. A cobertura efetivamente medida pertence aos relatórios de cada execução; não confundir o limite exigido com o resultado observado.

Na [CI de referência do código entregue](https://github.com/oficina-mecanica-soat/oficina-mecanica-execucao/actions/runs/43952065682), foram aprovados **361 testes**, sem falhas ou ignorados, com **95,38% de cobertura global de linhas** (1.526 de 1.600 linhas). Os relatórios estão no artefato `test-and-coverage-results`. Uma CI somente documental não representa nova medição.

Testes de integração exigem Docker e DynamoDB Local; SQS simulado e transporte por arquivos não comprovam IAM, FIFO/DLQ ou rede AWS.

<a id="configuração-de-development"></a>

## ⚙️ Configuração de development

| Configuração | Responsabilidade |
| --- | --- |
| AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN | Secrets temporários do runner; nunca enviados aos Pods. |
| TERRAFORM_STATE_KEY | Secret exclusivo de 256 bits em 64 caracteres hexadecimais; preservar backup e não trocar enquanto houver estados dependentes. |
| JWT_SECRET | Mesma chave compartilhada pelos serviços do ambiente; issuer/audience devem respeitar o contrato comum. |
| PROCESSAMENTO_REMOTO_HABILITADO | Variable que ativa o modo remoto no próximo apply; não autoriza AWS por si só. |
| AWS_DEPLOY_ENABLED | Controle da janela de implantação real. |
| AUTO_PR_ENABLED | Controle de criação dos PRs de promoção, sem autoaprovação. |

O workload usa namespace próprio, Service NodePort 30081 e listener privado 8081. Credenciais do runner não são enviadas aos Pods; a identidade LabRole compartilhada é a exceção acadêmica da ADR-0025. A criação da tabela e filas pertence a esta esteira; EKS/ECR e NLB pertencem à plataforma Kubernetes.

<a id="dados-e-mensageria"></a>

## 📨 Dados e mensageria

| Recurso | Nome em development | Configuração |
| --- | --- | --- |
| DynamoDB | oficina-mecanica-execucao-development | PAY_PER_REQUEST; PK/SK string; estado, deduplicação e outbox na transação local. |
| Entrada | oficina-mecanica-execucao-development.fifo | FIFO, visibility 60 s, long polling 20 s, retenção 4 dias e maxReceiveCount 5. |
| DLQ | oficina-mecanica-execucao-development-dlq.fifo | FIFO, retenção 14 dias, redrive somente da entrada própria. |

O consumidor processa PrepararExecucao, CancelarPreparacao e AutorizarExecucao. O produtor usa MessageDeduplicationId=mensagemId e MessageGroupId=ordemServicoId. Confirmação SQS ocorre após commit de estado, deduplicação e outbox; a deduplicação do transporte não substitui a idempotência de negócio. Falha técnica preserva a mensagem para repetição; erro de domínio não gera compensação fictícia.

Os seis contratos String em `/oficina-mecanica/{ambiente}/execucao/` são `dynamodb_table_name`, `dynamodb_table_arn`, `entrada_queue_url`, `entrada_queue_arn`, `dlq_url` e `dlq_arn`. O marcador `status/execucao=ready` comprova infraestrutura; não substitui saúde do workload ou prova de consumo. Campos secretos não são publicados nesses contratos.

<a id="operação-individual-da-dlq"></a>

## 🔎 Operação individual da DLQ

Somente em janela autorizada, usar o workflow AWS Deploy existente, na ref develop e Environment development, com a mesma concorrência do deploy. Não consumir, reenviar, remover ou purgar mensagens por comandos locais. Entradas: dlq_action, mensagem_id, corpo_sha256 e conta_aws. O digest identifica os bytes UTF-8 originais, sem publicar payload, receipt ou credenciais.

| Ação | Efeito |
| --- | --- |
| inspecionar | Confere conta/contratos e procura o ID/digest em lote limitado; valida o envelope pela Application e restaura visibilidade. Não envia ou exclui. |
| reenviar | Envia o corpo original com os mesmos IDs FIFO; mantém a cópia na DLQ. Aceitação de transporte não comprova commit. |
| confirmar-remover | Exige marcador RECEBIDA com revisão/hash compatíveis por leitura consistente, recebe novamente e reconfere ID/digest antes de excluir com receipt atual. |

Revisar cada resultado antes da ação seguinte. Inspeção limitada sem encontrar a mensagem não comprova DLQ vazia nem exclusão. Em falha ou divergência, não trocar IDs/payload para forçar sucesso; investigar e reconciliar. Leituras individuais não formam snapshot: a unidade de trabalho confere revisões atomicamente antes de confirmar transições ou propagar uma recusa concorrente. Reenvio não restaura posição histórica na FIFO nem contorna versão, cancelamento ou início manual.

<a id="observabilidade"></a>

## 📊 Observabilidade

A emissão HTTP usa o middleware oficial do Serilog configurado como na API principal, com rota parametrizada, correlação e níveis consistentes. Healthcheck saudável não gera ruído no nível normal; avisos e erros permanecem visíveis. A origem do logger é SourceContext; mensagens das bibliotecas são preservadas, e SQL rotineiro fica fora do nível normal. Consulte a [convenção da RFC-0006](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/architecture/rfcs/rfc-0006-observabilidade-centralizada-com-datadog.md).

O Deployment segue as tags env/service/version da API e a admissão Datadog existente; não instala Agent adicional. A versão acompanha o SHA da imagem. Envelope e outbox preservam o contexto de trace validado entre publicação, consumo e reinício; X-Correlation-Id é correlação funcional independente e não participa do hash de negócio.

Logs JSON e spans registram serviço, operação, duração e IDs técnicos sem Authorization, credenciais, corpos ou documentos. Os IDs de OS/Saga servem à investigação em logs/traces, sem criar dimensão de métrica por entidade. Contagem de tentativas não equivale a volume único de negócio. O painel compartilhado e a prova de correlação seguem o roteiro da API; métricas ausentes não significam zero e testes sem profiler não comprovam exportação APM.

<a id="operação-e-documentação"></a>

## 🧭 Operação e documentação

- [Visão geral, arquitetura e ordem das esteiras](https://github.com/oficina-mecanica-soat/oficina-mecanica-api#deploy-aws).
- [Configuração, primeira aplicação e recuperação após Reset](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/deploy/deploy-aws.md).
- [Collection consolidada e ambientes Local/Development](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/evidencias/postman.md).

Todo provisionamento, atualização e destroy na AWS ocorre pelas esteiras oficiais. O controle é `infra/terraform/environments/dev/terraform-action.env`; destroy exige alteração dedicada e verificação dos consumidores. `AWS_DEPLOY_ENABLED` autoriza a janela real, e `AUTO_PR_ENABLED` controla somente a abertura de promoções. Alterar uma Variable não dispara o CD.

Um ambiente disponível pode ser testado sem destroy. Sem estado compatível, alguns owners exigem primeiro uma recuperação por destroy dedicado com inventário vazio comprovado. Não apague recursos ou caches para contornar o bloqueio; consulte o guia antes de iniciar uma conta ou retomar um laboratório.

Fluxo Git: branch de trabalho → PR para `develop` → `release` → `main`. O CD físico ocorre em development; release e production registram promoções lógicas. PRs e merges permanecem sujeitos aos checks e à revisão. Mudanças somente em README/docs não exigem deploy físico pelos detectores atuais.
