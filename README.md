# Oficina Mecânica — Execução

Serviço responsável por preparação, reserva e baixa de estoque, autorização persistida e ações físicas do mecânico. A OS coordena a Saga; Orçamento/Pagamento decide o orçamento e confirma a cobrança. Cada serviço possui dados próprios e se comunica pelos contratos da RFC-0007, sem consultar o banco dos demais.

## Arquitetura e referências

A organização segue a API principal e preserva Clean Architecture, DDD, SOLID, KISS e DRY. Distribuição em microsserviços não substitui a separação interna em camadas.

| Camada | Responsabilidade |
| --- | --- |
| Domain | Agregados, estoque, preparação, execução, invariantes e abstrações de domínio. |
| Application | Casos de uso, validação, requests/responses, orquestração local e portas. |
| Infrastructure | DynamoDB, SQS, unidade de trabalho, persistência de Inbox/Outbox e telemetria. |
| API | HTTP, JWT, composição e workers; controllers delegam aos casos de uso. |

Fontes canônicas na API: [README](https://github.com/oficina-mecanica-soat/oficina-mecanica-api), [ADR-0024](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/architecture/adrs/adr-0024-evolucao-para-microsservicos.md), [ADR-0025](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/architecture/adrs/adr-0025-bancos-e-mensageria-aws.md), [ADR-0026](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/architecture/adrs/adr-0026-saga-orquestrada.md) e [RFC-0007](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/architecture/rfcs/rfc-0007-evolucao-para-microsservicos.md). Progresso, resultados datados e limitações de aceite ficam somente no [backlog canônico](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/projeto/backlog-tecnico.md).

## Ciclo persistido de execução

`PrepararExecucao.servicos` exige `servicoId` (instância na OS) e `servicoCatalogoId` (referência ao catálogo), conforme a RFC-0007. Ambos são preservados no resultado de preparação e no estado persistido. Não há geração de outro ID nem consulta ao catálogo da OS. Mensagens sem os dois identificadores são rejeitadas. Preparações persistidas sem vínculo de catálogo exigem reconciliação, sem preenchimento automático ou migração destrutiva.

`AutorizarExecucao` v1 persiste a autorização e os serviços pendentes junto da deduplicação, sem iniciar reparo ou publicar evento de início. Sua versão de orçamento é independente da versão da preparação; a Saga de OS continua responsável por validar a associação com o orçamento aprovado. Reentrega idêntica não reativa preparação cancelada nem troca orçamento/Saga. Autorização conflitante, preparação recusada, cancelada, sem serviços ou de outra OS não avançam.

`IniciarExecucaoServicoUseCase`, `FinalizarServicoUseCase` e `FinalizarExecucaoUseCase` seguem Request/Validator/UseCase e resposta tipada, reaproveitando `Servico`, `Estoque.BaixarItens`, unidade de trabalho e outbox. São entradas da Application, reutilizadas pelo HTTP local descrito abaixo, sem novos comandos SQS. Recebem `OperacaoId` estável na repetição, OS/preparação e serviço quando aplicável. A entrada HTTP verifica JWT/perfil e exige esse identificador; o Gateway encaminha essas ações ao serviço proprietário.

O primeiro início manual confirma `ExecucaoIniciada` na mesma transação do estado. Outros serviços não repetem esse evento. Finalizar individualmente não baixa estoque nem conclui a execução. A finalização explícita exige todos os serviços concluídos, valida cada reserva e confirma, numa transação, baixa, marcadores `Baixada`, estado final, deduplicação e `ExecucaoFinalizada` com UTC. Repetição preserva IDs/datas/eventos e não repete a baixa. O agregado físico `Execucao` reúne essas invariantes sem copiar o agregado/Saga de OS.

Cancelamento ainda pode liberar uma preparação autorizada sem início físico. Após o primeiro início, não libera estoque nem emite compensação fictícia. Cancelamento e início comparam a mesma revisão do documento da preparação, impedindo que os dois confirmem. Falha técnica mantém a operação pendente; não se torna decisão de negócio. Uma execução finalizada mantém a barreira contra nova preparação da mesma OS. Eventos conservam OS/Saga/correlação da autorização e usam a operação manual como `causationId`.

`ProcessadorPreparacao` aceita o novo comando de autorização; sua porta interna admite resultado imediato nulo, pois confirmar o comando não equivale a publicar evento. Consumidores local/SQS continuam confirmando somente depois do commit. O modelo físico da tabela, chaves e limite de 46 itens permanecem; autorização/serviços ficam no documento da preparação. Não cria tabela, índice, fila, SSM ou papel IAM.


Validação local do ciclo: **220 testes aprovados, zero ignorados**, cobertura consolidada por classe/linha de **96,92%**, restore/build Release sem avisos/erros e formato verificado. Inclui disputas com commits sincronizados, falha antes do commit, reserva inconsistente, autorização/reentrega sem evento fictício, retomada persistida e saldo após baixa. DynamoDB Local é real; SQS e identidade AWS não foram validados em laboratório. O CI confirma os gates e sua cobertura consolidada.

## HTTP — ações do mecânico

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

Artefatos locais: [OpenAPI](docs/oficina-mecanica-execucao-openapi.json) e [collection Postman](docs/oficina-mecanica-execucao.postman_collection.json). Importar a collection, preencher `jwt`, IDs e os três IDs de operação. Nenhuma credencial está embutida e não há script de geração de token. Primeiro preparar/autorizar pela mensageria local; a collection não simula autorização da Saga. O Compose habilita o modo local em Development; fora dele, configure o modo explicitamente conforme Execução local.

## Entrada de estoque no serviço proprietário

`POST /api/v1/execucoes/estoque/registrar-entrada` permite aos perfis Administrador e Atendente registrar a entrada de peças no estoque da filial. O corpo contém `operacaoId`, `filialId`, `pecaInsumoCatalogoId` e `quantidade` positiva. A rota é encaminhada pelo ingresso de Execução já existente no Gateway.

A regra `Estoque.RegistrarEntrada`, extraída da API original, permanece no Domain. Application valida e coordena; Infrastructure confirma saldo e identificação da entrada na mesma transação DynamoDB. Repetir o mesmo `operacaoId` e conteúdo retorna o resultado original, sem nova soma; conteúdo diferente retorna conflito. A operação preserva quantidades reservadas e usa as mesmas revisões de concorrência da preparação. Não escreve no estoque legado de OS, não acessa seu banco ou valida catálogo por leitura cruzada. Antes da chamada, selecionar filial e item válidos pelo catálogo público responsável.

## Execução local

Pré-requisitos: .NET SDK 10 e Docker Desktop. O Compose sobe API em 5094 e DynamoDB Local em `127.0.0.1:8000`, sem AWS ou SQL Server. DynamoDB Local usa credenciais fictícias definidas no cliente local, sem consultar credenciais do laboratório. Os dados do DynamoDB desta configuração são voláteis e permanecem enquanto seu container estiver ativo; reiniciar apenas a API preserva o estado, mas `compose down` o remove.

```powershell
Copy-Item .env.example .env
# Preencha JWT_SECRET com uma chave local de pelo menos 32 bytes.
docker compose --env-file .env up -d --build
curl.exe --fail http://localhost:5094/api/health
docker compose down
```

Resposta esperada: `Healthy`. A porta 5094 evita conflito com a API atual em 5093. O processo recusa inicialização sem issuer, audience e chave válida. O healthcheck é público e não comprova persistência. As rotas manuais e suas condições de disponibilidade estão descritas na seção HTTP local.

O worker lê envelopes JSON completos em `.local/mensagens/entrada/`. Após commit, arquiva em `processadas/`; resultados da outbox aparecem em `saida/`, com o `causationId` do comando original. Contratos inválidos vão para `investigacao/`, com código sanitizado; falhas técnicas preservam a entrada para repetição. Crie arquivos como `.tmp` e renomeie para `.json` só após terminar a gravação. Diretórios locais e `.env` não são versionados nem enviados à imagem.

Os testes de integração existentes validam reserva, reentrega, compensação e persistência usando DynamoDB Local em Docker, sem credenciais AWS. Execute a suíte descrita em Testes e qualidade. O Compose permanece disponível para inspeção manual; arquivos substituem somente o transporte e não comprovam FIFO/DLQ/IAM/SQS reais.

Sem Docker, defina `Jwt__Secret`, `Jwt__Issuer` e `Jwt__Audience` no ambiente local e execute:

```powershell
dotnet run --project src/OficinaMecanica.Execucao.API --urls http://localhost:5094
```

Por padrão, fora do Compose, o worker local está desligado. Para usar DynamoDB Local já iniciado, configure `ASPNETCORE_ENVIRONMENT=Development`, `ProcessamentoLocal__Habilitado=true`, `ProcessamentoLocal__Endpoint=http://localhost:8000`, `ProcessamentoLocal__Tabela` e `ProcessamentoLocal__Diretorio`. O modo local recusa endpoints remotos. Tabelas reais devem ser provisionadas pela esteira oficial; essa preparação cria somente tabela local.

O Compose usa issuer `oficina-mecanica-auth` e audience `oficina-mecanica-api`, conforme o contrato atual de Cliente. Ao integrar serviços, usar issuer, audience e chave compartilhados do mesmo ambiente; a autorização continua dependendo do perfil.

## Testes e qualidade

```powershell
dotnet restore OficinaMecanica.Execucao.sln
dotnet build OficinaMecanica.Execucao.sln -c Release --no-restore -warnaserror
dotnet format OficinaMecanica.Execucao.sln --verify-no-changes --no-restore
dotnet test OficinaMecanica.Execucao.sln -c Release --no-build --collect:"XPlat Code Coverage" --settings tests/sonarqube.runsettings --results-directory TestResults --logger trx
```

A CI segue a referência da API: Git Flow, build Release com analisadores e avisos como erros, formato, testes sem ignorados, cobertura mínima de **90%**, Docker, manifests Kubernetes e Terraform. O mínimo acadêmico de 80% não reduz o gate do projeto. A cobertura efetivamente medida pertence aos relatórios de cada execução; não confundir o limite exigido com o resultado observado.

Na [CI de referência do código entregue](https://github.com/oficina-mecanica-soat/oficina-mecanica-execucao/actions/runs/43806755609), foram aprovados **354 testes**, sem falhas ou ignorados, com **95,25% de cobertura global de linhas**. Os relatórios estão no artefato `test-and-coverage-results`. Uma CI somente documental não representa nova medição.

Testes de integração exigem Docker e DynamoDB Local; SQS simulado e transporte por arquivos não comprovam IAM, FIFO/DLQ ou rede AWS. A validação integrada dos três serviços usa o [roteiro canônico](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/docs/evidencias/validacao-integrada.md), incluindo rejeição sem saldo, compensação e início manual.

## CI/CD e implantação

Seguir a [ordem de apply da solução](https://github.com/oficina-mecanica-soat/oficina-mecanica-api#deploy-aws): Execução depende da VPC, plataforma Kubernetes e primeira implantação da API/OS, que publica sua fila de resultados. O Gateway é implantado após os três participantes estarem prontos. Destroy segue a ordem inversa e os bloqueios de consumidores/mensagens pendentes.

Fluxo Git: branch de trabalho → PR para develop → promoção para release → main. CI valida o PR; CD em development usa o plano salvo e a mesma imagem aprovada pela CI, sem rebuild alternativo. Artefato expirado ou conteúdo divergente bloqueiam a publicação. Nenhum workflow aprova PRs. A criação automática de promoções depende dos gates e de AUTO_PR_ENABLED; os merges permanecem sujeitos à governança.

Todo apply, destroy e ajuste real ocorre pelas esteiras oficiais. Consultas locais podem validar recursos, identidade e logs; não podem substituir o CD. Uma alteração documental não autoriza implantação. O controle versionado `infra/terraform/environments/dev/terraform-action.env` define apply/destroy; destroy exige alteração dedicada e reconciliação segura do state. Credencial expirada, acesso negado ou falha de rede não comprovam ausência de recurso.

| Configuração | Responsabilidade |
| --- | --- |
| AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN | Secrets temporários do runner; nunca enviados aos Pods. |
| TERRAFORM_STATE_KEY | Secret exclusivo de 256 bits em 64 caracteres hexadecimais; preservar backup e não trocar enquanto houver estados dependentes. |
| JWT_SECRET | Mesma chave compartilhada pelos serviços do ambiente; issuer/audience devem respeitar o contrato comum. |
| PROCESSAMENTO_REMOTO_HABILITADO | Variable que ativa o modo remoto no próximo apply; não autoriza AWS por si só. |
| AWS_DEPLOY_ENABLED | Controle da janela de implantação real. |
| AUTO_PR_ENABLED | Controle de criação dos PRs de promoção, sem autoaprovação. |

### Workload e ingresso privado

O apply cria namespace, Deployment, ConfigMap, Secret e Service próprios no cluster compartilhado. O Service usa porta HTTP 80, contêiner 8080 e NodePort contratual 30081. Kubernetes mantém o listener privado 8081 no NLB existente; Gateway encaminha as rotas públicas de Execução. Este serviço não cria outro Load Balancer, EKS ou regras públicas por CIDR.

O CD lê os contratos Kubernetes/SSM, valida conta/região, registry e fila OS real antes do plano. O modo remoto exige fila de resultados OS compatível; não utiliza URL manual ou outra conta por fallback. Rollout, imagem esperada e health precisam ser conferidos, além dos marcadores de infraestrutura.

### Identidade acadêmica

O runtime usa a exceção LabRole documentada na ADR-0025: credenciais do instance profile, com GetCallerIdentity pela mesma fonte e validação da conta/role esperadas antes de construir os clientes. Não recebe as credenciais do runner nem usa fallback para perfil local. Domain e Application não dependem do SDK de autenticação.

A role compartilhada não fornece isolamento IAM entre Pods com acesso ao IMDS. A plataforma exige IMDSv2 obrigatório/hop limit 2 nos nós efetivos; na retomada Academy, conferir a recuperação pela esteira Kubernetes. Não modificar trust policies ou enfraquecer autenticação como atalho.

### States e recuperação

Planos e states são cifrados antes da publicação em artifacts/caches. Falha de autenticação bloqueia restauração, sem fallback para cache legado. Se o cluster foi removido por Reset, a recuperação declarativa existente exige ausência comprovada e identidade compatível antes de retirar do state os recursos Kubernetes ausentes; não editar state manualmente nem recriar infraestrutura como atalho. Consultar os workflows e a RFC-0007 antes de executar.

## Dados e mensageria

| Recurso | Nome em development | Configuração |
| --- | --- | --- |
| DynamoDB | oficina-mecanica-execucao-development | PAY_PER_REQUEST; PK/SK string; estado, deduplicação e outbox na transação local. |
| Entrada | oficina-mecanica-execucao-development.fifo | FIFO, visibility 60 s, long polling 20 s, retenção 4 dias e maxReceiveCount 5. |
| DLQ | oficina-mecanica-execucao-development-dlq.fifo | FIFO, retenção 14 dias, redrive somente da entrada própria. |

O consumidor processa PrepararExecucao, CancelarPreparacao e AutorizarExecucao. O produtor usa MessageDeduplicationId=mensagemId e MessageGroupId=ordemServicoId. Confirmação SQS ocorre após commit de estado, deduplicação e outbox; a deduplicação do transporte não substitui a idempotência de negócio. Falha técnica preserva a mensagem para repetição; erro de domínio não gera compensação fictícia.

Os seis contratos String em `/oficina-mecanica/{ambiente}/execucao/` são `dynamodb_table_name`, `dynamodb_table_arn`, `entrada_queue_url`, `entrada_queue_arn`, `dlq_url` e `dlq_arn`. O marcador `status/execucao=ready` comprova infraestrutura; não substitui saúde do workload ou prova de consumo. Campos secretos não são publicados nesses contratos.

## Operação individual da DLQ

Somente em janela autorizada, usar o workflow AWS Deploy existente, na ref develop e Environment development, com a mesma concorrência do deploy. Não consumir, reenviar, remover ou purgar mensagens por comandos locais. Entradas: dlq_action, mensagem_id, corpo_sha256 e conta_aws. O digest identifica os bytes UTF-8 originais, sem publicar payload, receipt ou credenciais.

| Ação | Efeito |
| --- | --- |
| inspecionar | Confere conta/contratos e procura o ID/digest em lote limitado; valida o envelope pela Application e restaura visibilidade. Não envia ou exclui. |
| reenviar | Envia o corpo original com os mesmos IDs FIFO; mantém a cópia na DLQ. Aceitação de transporte não comprova commit. |
| confirmar-remover | Exige marcador RECEBIDA com revisão/hash compatíveis por leitura consistente, recebe novamente e reconfere ID/digest antes de excluir com receipt atual. |

Revisar cada resultado antes da ação seguinte. Inspeção limitada sem encontrar a mensagem não comprova DLQ vazia nem exclusão. Em falha ou divergência, não trocar IDs/payload para forçar sucesso; investigar e reconciliar. Leituras individuais não formam snapshot: a unidade de trabalho confere revisões atomicamente antes de confirmar transições ou propagar uma recusa concorrente. Reenvio não restaura posição histórica na FIFO nem contorna versão, cancelamento ou início manual.

## Observabilidade

O Deployment segue as tags env/service/version da API e a admissão Datadog existente; não instala Agent adicional. A versão acompanha o SHA da imagem. Envelope e outbox preservam o contexto de trace validado entre publicação, consumo e reinício; X-Correlation-Id é correlação funcional independente e não participa do hash de negócio.

Logs JSON e spans registram serviço, operação, duração e IDs técnicos sem Authorization, credenciais, corpos ou documentos. Os IDs de OS/Saga servem à investigação em logs/traces, sem criar dimensão de métrica por entidade. Contagem de tentativas não equivale a volume único de negócio. O painel compartilhado e a prova de correlação seguem o roteiro da API; métricas ausentes não significam zero e testes sem profiler não comprovam exportação APM.
