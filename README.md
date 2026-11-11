# Oficina Mecânica — Execução

Serviço de diagnóstico, estoque físico, reservas e execução da Fase 4. Responsável: Gabriel. A arquitetura é definida pelas ADRs 0024–0026 e pela RFC-0007 do repositório da API.

## Estado desta entrega — ciclo persistido da F4-05

Serviço .NET 10 com Clean Architecture, healthcheck, validação JWT, testes, Dockerfile, manifests Kubernetes, workload Terraform e CI/CD próprios. Domain mantém `Estoque`/`ItemEstoque` e as regras de reserva; Application separa preparação e cancelamento em Request/Response/Validator/UseCase, com portas tipadas. Infrastructure implementa persistência, unidade de trabalho DynamoDB, outbox e processamento local por arquivos; API configura HTTP e a composição das dependências.

`PrepararExecucao` reserva todo o lote ou persiste uma recusa sem reserva parcial. `CancelarPreparacao` libera o saldo uma vez, inclusive após reiniciar o processamento e reentregar com outro `mensagemId`. Estado, saldos, reservas por OS/item, deduplicação e outbox são confirmados na mesma transação, com comparação de revisões técnicas. A versão de negócio segue a RFC-0007 atualizada no [PR #325](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/pull/325).

O [mapeamento, modelo de dados e limites do incremento](docs/extracao-preparacao.md) detalha as garantias e evidências. O Domain também contém `Servico`, `StatusServico` e as mensagens/testes extraídos da API, preservando criação, início e finalização individuais. A entidade agora participa do ciclo persistido: autorização, início manual de cada serviço, finalização individual e finalização explícita com baixa. As ações manuais são casos de uso internos registrados na DI; ainda não há rotas HTTP dessas ações, corte público ou integração ponta a ponta com OS/cobrança. A configuração Terraform de DynamoDB, SQS FIFO e DLQ da F4-04 está preparada; os recursos reais ainda não foram provisionados nem conectados ao runtime. O healthcheck verifica somente a aplicação; não comprova acesso a banco ou filas. O worker por arquivos só pode ser habilitado em Development e não é consumidor SQS. A composição remota dos adaptadores está preparada por chamada explícita, mas não foi ativada pelo host ou workload.

### Extração de Servico — marco anterior da F4-05

Foram reaproveitados `Servico`, `StatusServico` (valores 1/2/3), quatro mensagens de validação e os dez cenários de teste correspondentes da API. Preservam IDs locais, referência ao catálogo, valor positivo, transições Pendente → EmExecucao → Finalizado e datas UTC. As mudanças são de namespace e de isolamento dos testes: os helpers mínimos ficam no próprio teste, sem copiar o agregado/factory de OS. Não há referência entre projetos Domain dos serviços nem consulta ao catálogo/banco da OS.

A RFC-0007 define `AutorizarExecucao` e os eventos de início/finalização, enquanto o fluxo atual distingue aprovação e ações individuais do mecânico. Gabriel confirmou a preservação do início manual; o esclarecimento foi integrado na API #335. O incremento anterior preservou as regras de `Servico` sem handlers ou eventos. A seção abaixo descreve a conexão posterior dessas regras à persistência e outbox. O monólito permanece com seu fluxo público até o corte coordenado com OS/Gateway.

Validação local desta extração: **190 testes aprovados, zero ignorados**, cobertura consolidada por classe/linha de **96,70%**, restore/build Release sem avisos/erros e formato verificado. Comparação com a origem confirmou equivalência de `Servico`/`StatusServico` exceto namespace/formato e preservação dos cenários originais. O CI deve confirmar os mesmos gates antes do merge.

Nenhuma alteração desta entrega implementa a extração no monólito. Catálogo, clientes, veículos e coordenação da Saga continuam no serviço de OS. Não há acesso ao banco de outro serviço nem compartilhamento de Domain/migrations.

## Ciclo persistido de execução — sem corte HTTP ou ativação AWS

`PrepararExecucao.servicos` passa a exigir `servicoId` (instância na OS) e `servicoCatalogoId` (referência ao catálogo), conforme alinhamento explícito com Gabriel e PR próprio da RFC-0007. Ambos são preservados no resultado de preparação e no estado persistido. Não há geração de outro ID nem consulta ao catálogo da OS. Os produtores da F4-06 precisam enviar o novo campo antes do corte; mensagens antigas sem ele são rejeitadas. Preparações persistidas sem vínculo de catálogo exigem reconciliação, sem preenchimento automático ou migração destrutiva.

`AutorizarExecucao` v1 persiste a autorização e os serviços pendentes junto da deduplicação, sem iniciar reparo ou publicar evento de início. Sua versão de orçamento é independente da versão da preparação; a Saga de OS continua responsável por validar a associação com o orçamento aprovado. Reentrega idêntica não reativa preparação cancelada nem troca orçamento/Saga. Autorização conflitante, preparação recusada, cancelada, sem serviços ou de outra OS não avançam.

`IniciarExecucaoServicoUseCase`, `FinalizarServicoUseCase` e `FinalizarExecucaoUseCase` seguem Request/Validator/UseCase e resposta tipada, reaproveitando `Servico`, `Estoque.BaixarItens`, unidade de trabalho e outbox. São entradas internas da Application, não novos comandos SQS ou endpoints públicos. Recebem `OperacaoId` estável na repetição, OS/preparação e serviço quando aplicável. A integração HTTP/autorização deverá estabelecer esse identificador e verificar a identidade/papel do mecânico; a DI não substitui essa validação.

O primeiro início manual confirma `ExecucaoIniciada` na mesma transação do estado. Outros serviços não repetem esse evento. Finalizar individualmente não baixa estoque nem conclui a execução. A finalização explícita exige todos os serviços concluídos, valida cada reserva e confirma, numa transação, baixa, marcadores `Baixada`, estado final, deduplicação e `ExecucaoFinalizada` com UTC. Repetição preserva IDs/datas/eventos e não repete a baixa. O agregado físico `Execucao` reúne essas invariantes sem copiar o agregado/Saga de OS.

Cancelamento ainda pode liberar uma preparação autorizada sem início físico. Após o primeiro início, não libera estoque nem emite compensação fictícia. Cancelamento e início comparam a mesma revisão do documento da preparação, impedindo que os dois confirmem. Falha técnica mantém a operação pendente; não se torna decisão de negócio. Uma execução finalizada mantém a barreira contra nova preparação da mesma OS. Eventos conservam OS/Saga/correlação da autorização e usam a operação manual como `causationId`.

`ProcessadorPreparacao` aceita o novo comando de autorização; sua porta interna admite resultado imediato nulo, pois confirmar o comando não equivale a publicar evento. Consumidores local/SQS continuam confirmando somente depois do commit. O modelo físico da tabela, chaves e limite de 46 itens permanecem; autorização/serviços ficam no documento da preparação. Não cria tabela, índice, fila, SSM ou papel IAM.

O host remoto, `Program.cs`, Terraform, manifests e workflows não são alterados nem ativados. `AWS_DEPLOY_ENABLED=false` permanece. O merge integra código e executa CI; não implanta recursos. Ainda faltam políticas/rotas e corte coordenado com OS/Gateway, observabilidade distribuída, identidade IAM e prova real no laboratório. A F4-05/F4-10 não está concluída apenas por esses testes locais.

Validação local do ciclo: **220 testes aprovados, zero ignorados**, cobertura consolidada por classe/linha de **96,92%**, restore/build Release sem avisos/erros e formato verificado. Inclui disputas com commits sincronizados, falha antes do commit, reserva inconsistente, autorização/reentrega sem evento fictício, retomada persistida e saldo após baixa. DynamoDB Local é real; SQS e identidade AWS não foram validados em laboratório. O CI confirma os gates e sua cobertura consolidada.

## Fontes da verdade

- [Plano completo de execução, padronização de esteiras e resumo do PR #2](docs/plano-execucao-gabriel.md)

- [README principal e funcionamento atual](https://github.com/oficina-mecanica-soat/oficina-mecanica-api)
- [Plano de execução aprovado](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/186c601b1bf5ef2bb970c728a5ebe99d43bb8dce/docs/projeto/backlog-tecnico.md)
- [ADR-0024 — limites dos serviços](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/main/docs/architecture/adrs/adr-0024-evolucao-para-microsservicos.md)
- [ADR-0025 — dados e mensageria](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/main/docs/architecture/adrs/adr-0025-bancos-e-mensageria-aws.md)
- [ADR-0026 — Saga](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/main/docs/architecture/adrs/adr-0026-saga-orquestrada.md)
- [RFC-0007 — contratos v1](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/main/docs/architecture/rfcs/rfc-0007-evolucao-para-microsservicos.md)

## Execução local

Pré-requisitos: .NET SDK 10 e Docker Desktop. O Compose sobe API em 5094 e DynamoDB Local em `127.0.0.1:8000`, sem AWS ou SQL Server. DynamoDB Local usa credenciais fictícias definidas no cliente local, sem consultar credenciais do laboratório. Os dados do DynamoDB desta configuração são voláteis e permanecem enquanto seu container estiver ativo; reiniciar apenas a API preserva o estado, mas `compose down` o remove.

```powershell
Copy-Item .env.example .env
# Preencha JWT_SECRET com uma chave local de pelo menos 32 bytes.
docker compose --env-file .env up -d --build
curl.exe --fail http://localhost:5094/api/health
docker compose down
```

Resposta esperada: `Healthy`. A porta 5094 evita conflito com a API atual em 5093. O processo recusa inicialização sem issuer, audience e chave válida. O healthcheck é público; endpoints de negócio e suas políticas serão implementados com as regras extraídas, sem inventar rotas de sucesso nesta base.

O worker lê envelopes JSON completos em `.local/mensagens/entrada/`. Após commit, arquiva em `processadas/`; resultados da outbox aparecem em `saida/`, com o `causationId` do comando original. Contratos inválidos vão para `investigacao/`, com código sanitizado; falhas técnicas preservam a entrada para repetição. Crie arquivos como `.tmp` e renomeie para `.json` só após terminar a gravação. Diretórios locais e `.env` não são versionados nem enviados à imagem.

A pasta de scripts de demonstração foi removida por padronização. Os testes de integração existentes validam reserva, reentrega, compensação e persistência usando DynamoDB Local em Docker, sem credenciais AWS. Execute a suíte descrita em Testes e qualidade. O Compose permanece disponível para inspeção manual; arquivos substituem somente o transporte e não comprovam FIFO/DLQ/IAM/SQS reais.

Sem Docker, defina `Jwt__Secret`, `Jwt__Issuer` e `Jwt__Audience` no ambiente local e execute:

```powershell
dotnet run --project src/OficinaMecanica.Execucao.API --urls http://localhost:5094
```

Por padrão, fora do Compose, o worker local está desligado. Para usar DynamoDB Local já iniciado, configure `ASPNETCORE_ENVIRONMENT=Development`, `ProcessamentoLocal__Habilitado=true`, `ProcessamentoLocal__Endpoint=http://localhost:8000`, `ProcessamentoLocal__Tabela` e `ProcessamentoLocal__Diretorio`. O modo local recusa endpoints remotos. Tabelas reais devem ser provisionadas pela esteira da F4-04; este bootstrap cria somente tabela local.

O Compose usa issuer `oficina-mecanica-auth` e audience `oficina-mecanica-api`, conforme o contrato atual de Cliente. Aceitar também a identidade interna da OS exige validar explicitamente os emissores e políticas na integração F4-09; não presumir que compartilhar chave resolve essa integração.

## Testes e qualidade

```powershell
dotnet restore OficinaMecanica.Execucao.sln
dotnet build OficinaMecanica.Execucao.sln -c Release --no-restore -warnaserror
dotnet format OficinaMecanica.Execucao.sln --verify-no-changes --no-restore
dotnet test OficinaMecanica.Execucao.sln -c Release --no-build --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory TestResults --logger trx
```

CI deriva da API: escopo documental, Git Flow, build, formato, testes sem ignorados, cobertura mínima de **90%** preservada da referência (acima dos 80% mínimos da Fase 4), Docker, manifests em Kind e Terraform. Analisadores .NET e avisos como erros fazem parte do build; a análise Sonar/equivalente exigida na F4-12 deve ser comprovada antes do aceite final. A lógica e os testes de recuperação de destroy derivados da API ficam nos próprios workflows; não constituem evidência de recuperação de um deploy deste serviço ainda inexistente.

Validação local em 10/11/2026: build sem avisos/erros; formato verificado; **11 testes .NET aprovados, zero ignorados e 100% de cobertura de linhas da base atual**; quatro testes Node de guardrails aprovados; Terraform 1.15.8 validado; imagem Docker construída e `GET /api/health` retornou `200 Healthy` em `localhost:5094`. Container e rede Compose encerrados após o teste. Esses números cobrem somente a base da F4-03, sem regras de estoque ou execução implementadas.

A reorganização da F4-05 passou em **147 testes locais, zero ignorados** (59 Domain, 12 Application, 11 API, 65 Infrastructure), com build Release sem avisos e formato validado. Cobertura local consolidada: **96,62%**; o gate de 90% deve ser confirmado também no CI.

A evidência acima da base é o marco histórico da F4-03. A validação da preparação/cancelamento e seus limites está em [extracao-preparacao.md](docs/extracao-preparacao.md). Testes de integração exigem Docker e falham se o container não puder iniciar; não são ignorados. Testcontainers usa a versão de referência do projeto com SSH.NET 2026.0.0 fixado somente no projeto de testes, corrigindo a dependência transitiva vulnerável sem suprimir os avisos do build.

## CI/CD e infraestrutura

Fluxo: `feature/* → PR develop → PR release → PR main`. As branches de estágio não recebem código diretamente. Promoções usam merge commit para preservar ancestralidade. Workflows próprios: CI, CD Development, AWS Deploy reutilizável, CD Release e CD Production. Homologation/production continuam promoções lógicas.

**AWS permanece desligada.** `AWS_DEPLOY_ENABLED=false` impede chamar o deploy no merge de código. Um push deployável sem execução AWS também não promove automaticamente para release. Alterações exclusivamente documentais preservam o fluxo de promoção sem AWS. Não habilitar o deploy nem trocar o controle operacional para provocar provisionamento sem autorização específica.

O workload Terraform mantém namespace próprio `oficina-mecanica-execucao`, Deployment, Service privado e Secret JWT, sem criar VPC, EKS, registry compartilhado ou banco alheio. Manifests `k8s/` são referência local/dry-run; na AWS o ownership é exclusivo do Terraform. O roteamento público será integrado na F4-09.

### Modelo de deploy e contratos consumidos

O workflow reutilizável segue os cinco jobs da API: `resolve_action`, `prepare_and_plan`, `build_and_push_image`, `apply_or_destroy` e `rollout_check`. Reutiliza a localização da CI do PR integrado, o artefato `execucao-image-pr-{numero}`, a comparação da árvore Git validada e a publicação da mesma imagem; o CD não recompila a imagem. Artefato ausente/expirado ou conteúdo diferente bloqueia o deploy; não há fallback para rebuild.

O plano Terraform é gerado uma vez e transferido como artefato da execução/tentativa, com retenção de um dia. O apply usa somente esse plano, após conferir a mesma conta AWS. Há bloqueio de mudanças em VPC/EKS/RDS/ECR/NLB compartilhados. O rollout mantém o Service privado atual e valida `/api/health`; não afirma prontidão de DynamoDB/SQS ainda não conectados.

Contratos do owner Kubernetes, aprovados no [PR #58](https://github.com/oficina-mecanica-soat/oficina-mecanica-infra-kubernetes/pull/58), sob `/oficina-mecanica/development`:

| Sufixo SSM | Uso |
| --- | --- |
| `status/kubernetes` | Deve conter `ready`. |
| `kubernetes/cluster_name` | Cluster compartilhado; não é criado neste serviço. |
| `kubernetes/execucao_ecr_repository_name` | Deve identificar `oficina-mecanica-execucao`. |
| `kubernetes/execucao_ecr_repository_url` | Deve coincidir com o ECR real da conta/região autenticada. |

A variable `ECR_REPOSITORY_NAME` deixa de ser consumida. A infraestrutura publica os contratos somente no próximo apply autorizado; o merge do PR #58 não comprova sua existência na AWS.

Dependências antes do primeiro deploy autorizado:

1. Avisar Gabriel para criar o laboratório e atualizar os environments; manter `AWS_DEPLOY_ENABLED=false` até a janela autorizada.
2. Seguir ADR-0012 para os caches da conta nova: apply sem state restaurado é bloqueado. Bootstrap somente por destroy dedicado em conta comprovadamente sem recursos/contratos do projeto, antes de provisionar as dependências. Não há autorização por `ALLOW_INITIAL_STATE_BOOTSTRAP` nem exceção por ausência de namespace.
3. Provisionar VPC e Kubernetes/ECR pela ordem documentada e confirmar os quatro contratos acima. O workload não cria registry nem reaproveita o ECR da API.
4. Configurar Environment `development` com secrets `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`, `JWT_SECRET`; variables `AWS_REGION`, `JWT_ISSUER`, `JWT_AUDIENCE`. Confirmar emissor/audience com os consumidores sem divulgar valores sensíveis.
5. Somente com a janela autorizada e as dependências validadas, habilitar `AWS_DEPLOY_ENABLED`. Merges atuais executam CI e mantêm deploy e promoção de alteração implantável bloqueados.

O state local fica no cache do Actions isolado por serviço, conta e ambiente, seguindo a concessão acadêmica existente. Não há fallback entre contas. O Secret Kubernetes contém a chave no state e no plano salvo, como na concessão da API atual; `sensitive` oculta a apresentação, sem remover o valor desses arquivos. O tfvars temporário usa permissões restritas, é removido ao final e não é publicado como artefato. A evolução para runtime/Secrets Manager permanece no backlog aprovado.

Terraform 1.15.8:

```powershell
terraform fmt -check -recursive infra/terraform
terraform -chdir=infra/terraform/environments/dev init -backend=false -input=false
terraform -chdir=infra/terraform/environments/dev validate
```

Apply/destroy reais só pela esteira. `terraform-action.env` define a ação e permanece inalterado nesta padronização. Destroy exige alteração dedicada, state validado, plano exclusivo de remoção e state final sem recursos gerenciados. Os scripts de bootstrap/recuperação são os já existentes na referência.

O Terraform registra `data.aws_eks_cluster.shared` e a identidade da conta para permitir verificar a qual cluster o workload pertence, sem assumir ownership do EKS. O provider Kubernetes foi movido de `versions.tf` para `providers.tf`, preservando seu endereço; isso permite à recuperação substituir a configuração quando o EKS estiver comprovadamente ausente. Com cluster existente, permanece o destroy normal. Com `ResourceNotFoundException`, a recuperação usa `removed` com `destroy=false`; não altera state manualmente nem recria infraestrutura. Não tenta gerar kubeconfig de cluster confirmado ausente.

State legado sem identificação comprovável do cluster continua bloqueado para recuperação, exigindo reconciliação específica; não se presume ausência. AccessDenied, credencial inválida e falhas de rede não autorizam remoção. Cache é salvo também após falha parcial. Respeitar filas/outbox pendentes antes de destruir quando forem implementadas.

Validação local desta padronização: nove testes Node aprovados, incluindo contratos SSM/registry, preparação simulada e proteção de ownership; Terraform 1.15.8 com formatação, init sem backend e validate aprovados; actionlint 1.7.12 aprovou os workflows (sem ShellCheck/Pyflakes). Os testes não comprovam publicação ECR, plano/apply/recuperação ou rollout reais. O CI mantém os gates existentes e repete as validações do serviço.

Variables do repositório: `AWS_DEPLOY_ENABLED=false`, `AUTO_PR_ENABLED=true`, `RELEASE_BRANCH=release`. Secrets não são provisionados pelo bootstrap. O usuário `soat-architecture` deve ter acesso verificável antes da entrega; repositório público permite leitura, sem afirmar concessão administrativa.

## Próximo passo

F4-04/F4-05: validar a identidade AWS do workload, conectar o runtime ao DynamoDB/SQS reais e comprovar leitura/escrita/consumo pelas esteiras na janela autorizada; depois implementar autorização/início/finalização física. Atualizar contratos SSM na tarefa de infraestrutura e integrar com OS na F4-10. Observabilidade distribuída e métricas da Saga/DLQ serão evidenciadas na F4-11. O workload atual permanece sem consumidor remoto habilitado; não habilitar deploy para afirmar integração ainda não validada.

## Dados e mensageria — preparação da F4-04

ADR-0025 e RFC-0007 definem ownership e parâmetros operacionais. Este incremento propõe os nomes físicos abaixo, derivados do componente e do ambiente; não os apresenta como nomes anteriormente definidos pela RFC. A aprovação do PR aprova essa proposta antes do provisionamento.

| Recurso | Nome em development | Configuração |
| --- | --- | --- |
| DynamoDB | `oficina-mecanica-execucao-development` | `PAY_PER_REQUEST`, PK/SK string, compatível com `ArmazenamentoDynamoDb`; sem TTL de idempotência inventado, Scan ou índices sem acesso definido. |
| Entrada | `oficina-mecanica-execucao-development.fifo` | FIFO, visibility 60 s, long polling 20 s, retenção 345600 s (4 dias), maxReceiveCount 5. |
| DLQ | `oficina-mecanica-execucao-development-dlq.fifo` | FIFO, retenção 1209600 s (14 dias), redrive permitido somente da entrada própria. |

A deduplicação por conteúdo fica desligada; o produtor enviará `MessageDeduplicationId=mensagemId` e `MessageGroupId=ordemServicoId` no incremento de transporte. Este Terraform não implementa o produtor/consumidor. As filas usam SSE gerenciado pelo SQS, sem nova chave KMS ou alteração de IAM neste PR. O status de infraestrutura precede o Deployment no grafo Terraform.

Os seis contratos não sensíveis são parâmetros `String` em `/oficina-mecanica/{ambiente}/execucao/`, com os mesmos nomes no output `execucao_contracts`:

- `dynamodb_table_name` e `dynamodb_table_arn`;
- `entrada_queue_url` e `entrada_queue_arn`;
- `dlq_url` e `dlq_arn`.

O marcador `/oficina-mecanica/{ambiente}/status/execucao=ready` depende dos contratos e da política de redrive. Indica disponibilidade da infraestrutura; não comprova saúde da aplicação, identidade IAM ou consumo. `environment` é derivado do `ssm_environment_name` já usado pela esteira, evitando nomes silenciosamente presos a development.

### Validação e operação

O CI mantém os jobs/gates existentes e acrescenta `terraform test` com providers AWS/Kubernetes simulados, sem credenciais ou recursos reais, conforme o [mecanismo de mocks do Terraform](https://developer.hashicorp.com/terraform/language/tests/mocking). Quatro cenários verificam compatibilidade PK/SK, parâmetros FIFO/redrive da RFC, contratos SSM e segregação de nomes por ambiente. A validação local passou nesses quatro cenários e nos 15 testes Node; formatação/validação Terraform 1.15.8 e actionlint também passaram. Não confundir essa evidência com teste SQS real.

No futuro apply autorizado, a esteira também consultará metadados de tabela/filas e comparará os parâmetros SSM com os recursos registrados no state. O bootstrap em conta nova passa a conferir também DynamoDB/SQS; recursos existentes do projeto bloqueiam state vazio. Sem permissão de consulta, a operação falha em vez de assumir ausência.

Destroy continua dedicado e usa somente o plano salvo. Antes de planejar e imediatamente antes de aplicar, consulta a partição `OUTBOX#PENDENTE` de forma consistente e verifica contagens de mensagens visíveis/em voo/atrasadas na entrada e DLQ. Pendências, respostas inválidas ou falha de acesso bloqueiam. Contexto não sensível de tabela/filas acompanha o plano para conferir a remoção real e a limpeza dos contratos SSM mesmo depois de o state ficar vazio. Não remove mensagens nem faz purge/redrive automático.

**Antes de autorizar destroy, parar produtores/consumidores e preservar os dados necessários.** Contagens SQS são aproximadas e não substituem essa quiescência operacional. Mesmo sem pendências de transporte, destruir a tabela elimina estoque, reservas, histórico e deduplicação; o guardrail de outbox não equivale a backup ou migração. A implementação deste PR não executa essa destruição.

### O que permanece pendente

Não há alteração de IAM, nova role, credencial em código ou ativação implícita da role dos nodes. Identidade do workload, menor privilégio e capacidade real do AWS Academy precisam ser validados na janela autorizada, conforme ADR-0025; não presumir que a credencial do runner seja a identidade do Pod. Conectar consumidor/publicador SQS, usar AWS SDK no runtime e comprovar leitura/escrita/consumo pelo workload permanecem próximos incrementos. A F4-04 não está concluída com este preparo offline.

`AWS_DEPLOY_ENABLED=false` permanece preservado; nenhum plano autenticado, apply/destroy ou consulta AWS foi executado nesta entrega. Avisar Gabriel antes de precisar do novo laboratório e atualizar os environments somente nessa janela.

## Adaptadores SQS — preparação da integração

`TransporteSqs` implementa a porta existente `ITransporteResultado`: envia o envelope persistido para a fila da OS informada na construção, com `MessageGroupId=ordemServicoId` e `MessageDeduplicationId=mensagemId`. Não gera outro ID em repetição. O publicador de outbox existente continua responsável por lease, backoff e marcação de envio; sem confirmação válida do SDK, a publicação falha para repetição.

`ConsumidorSqs` recebe uma mensagem por vez, com long polling de 20 s e visibilidade de 60 s. Confere a correspondência dos UUIDs do envelope com os metadados FIFO, chama a porta `IProcessadorMensagem` (implementada pelo processador de preparação existente) e só exclui a entrada após concluir o processamento. A preparação/cancelamento já confirma estado, estoque, deduplicação e outbox atomicamente antes de retornar.

Durante processamento prolongado, renova a visibilidade a cada 20 s. Falha de renovação cancela o trabalho cooperativamente e impede confirmação. JSON/tipo/versão inválidos, falha de persistência, cancelamento e resposta incerta preservam a entrada; a política SQS encaminhará à DLQ após o limite de recebimentos. Não envia manualmente à DLQ, não faz purge e não presume entrega exatamente uma vez: reentrega continua dependendo da idempotência persistida. Falha de confirmação após commit pode repetir a entrada; falha após envio de resultado repete os mesmos metadados de deduplicação.

Os clientes `IAmazonSQS` são recebidos por injeção nos adaptadores. Eles não criam cliente AWS, não escolhem role e não carregam credenciais. O `AddInfrastructure` usado pelo host atual continua com o worker local, sem ativação SQS. A composição remota explícita está descrita abaixo; integrar esse código não conecta um Pod à AWS.

O destino de resultados pertence ao serviço de OS conforme RFC-0007. Sua URL deverá vir do contrato publicado pelo owner na integração; não reutilizar a entrada do Execução para resultados nem inventar um parâmetro SSM de OS neste repositório. Os nomes das URLs usadas nos testes são fictícios e exclusivos do cliente simulado.

Validação local: build Release sem avisos, suíte completa anterior com 170 testes aprovada e suíte unitária dos adaptadores com 22 testes aprovada após acrescentar a compatibilidade de UUIDs. O CI executa os 171 testes da solução atual e confirma a cobertura, preservando o gate de 90%. Dois cenários de integração usam DynamoDB Local real e SQS simulado: preparação/reentrega/cancelamento com confirmação somente após persistência, e retomada de outbox com a mesma deduplicação após envio confirmado e falha de gravação. São testes de contrato do adaptador; não comprovam comportamento FIFO/DLQ/IAM ou disponibilidade SQS reais.

Na entrega dos adaptadores, identidade IAM, composição do runtime, contrato da fila de resultados de OS, observabilidade e validação real permaneceram pendentes. O contrato de OS foi posteriormente aprovado no [PR #328 da API](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/pull/328), e a preparação da composição remota é detalhada a seguir. Nenhuma configuração de acesso AWS foi escolhida implicitamente. `AWS_DEPLOY_ENABLED=false` permanece ativo, sem consultas AWS, apply ou destroy. Avisar Gabriel quando a prova exigir novo laboratório.

## Composição remota — preparação sem ativação

`AddInfrastructureRemota(ProcessamentoRemotoOptions, IAmazonDynamoDB, IAmazonSQS)` combina `ArmazenamentoDynamoDb`, unidade de trabalho, `ProcessadorPreparacao`, `ConsumidorSqs`, `TransporteSqs` e `PublicadorOutbox` existentes. O host deve chamar `AddApplication`, prover logging e fornecer os dois clientes SDK já configurados com a identidade validada. O chamador mantém a responsabilidade de descartar esses clientes; a composição não os cria, não busca credenciais nem escolhe role. O registro local e o remoto são mutuamente exclusivos, incluindo registros repetidos.

As opções imutáveis recebem tabela, URL de entrada do Execução e URL de resultados da OS. A futura composição do host/deploy deverá obtê-las dos contratos aprovados, respectivamente `/oficina-mecanica/{ambiente}/execucao/dynamodb_table_name`, `/oficina-mecanica/{ambiente}/execucao/entrada_queue_url` e `/oficina-mecanica/{ambiente}/api/entrada_queue_url`. Não há novo parâmetro SSM. A composição exige tabela preenchida, URLs HTTPS FIFO e destinos distintos; não consulta SSM nem comprova que a URL pertence ao owner correto. Existência, conta, região e ownership precisam ser conferidos pela integração/deploy antes da ativação.

`ProcessamentoRemotoWorker` hospeda consumo e publicação em loops independentes, com escopos próprios por tentativa. O long polling não bloqueia outbox já confirmada. As garantias de transação, idempotência, confirmação após commit, renovação de visibilidade, lease e backoff continuam nos componentes existentes. Falhas preservam entrada/outbox e são registradas sem envelope ou detalhes sensíveis do SDK. O loop aguarda 500 ms quando não há trabalho ou ocorre falha; a repetição de publicação continua respeitando também o backoff persistido. O encerramento cancela ambos os loops e aguarda sua conclusão.

`Program.cs`, Compose, manifests, Terraform e workflows não ativam essa composição neste incremento. Não há variável de ambiente que a habilite, criação de tabela pelo worker remoto ou fallback para modo local. Ainda é necessário definir e comprovar a identidade IAM do workload e integrar a chamada explícita no host junto da configuração aprovada. O healthcheck atual continua verificando somente HTTP, sem comprovar DynamoDB/SQS. Esta preparação não conclui F4-04, F4-05 nem integração da Saga.

Validação local: **180 testes aprovados, zero ignorados**, cobertura consolidada por classe/linha de **96,83%**, build Release sem avisos/erros e formatação verificada. O CI confirma a consolidação pelo ReportGenerator e o gate de 90%. Os novos cenários cobrem ausência de ativação no host atual, bloqueio de composição mista/configuração inválida, retomada de outbox durante poll bloqueado, recuperação de falhas de recepção/envio, saldo persistido antes do ACK e cancelamento dos loops. DynamoDB Local é real; SQS é simulado. Não constituem prova de IAM, FIFO/DLQ ou execução em Pod na AWS.

## Estrutura obrigatória dos PRs

Validações operacionais e seus testes ficam nos YAMLs existentes de CI/CD. Não são versionadas pastas de scripts nem diretórios auxiliares adicionais. Trechos necessários à execução são gerados somente no diretório temporário do runner; não são enviados como conteúdo do repositório ou artefato de scripts.

O job obrigatório de Git Flow verifica essa estrutura em todos os PRs, inclusive documentais: bloqueia qualquer pasta chamada `scripts` e diretórios de topo fora da estrutura aprovada do projeto. Funcionalidades devem seguir as camadas existentes. Exceções de estrutura precisam de alinhamento explícito, sem remover o gate para contornar a regra. `AGENTS.md` e as orientações locais registram o mesmo requisito.

## Orientações para trabalho com Codex

O `AGENTS.md` mantém as regras obrigatórias e aponta para o procedimento canônico em `oficina-mecanica-api/.agents/skills/oficina-mecanica/SKILL.md`. A skill do Execução é somente um encaminhamento, evitando duas versões do mesmo fluxo. READMEs e documentos de arquitetura continuam sendo as fontes de decisões e comportamento do projeto.

Ao abrir um novo chat, prefira a raiz do repositório correto. Se o chat foi aberto fora do Git, peça a leitura explícita do `AGENTS.md` e da skill antes de implementar; entrar na pasta por um comando não garante descoberta automática. A verificação deste ajuste confirma os arquivos e os caminhos, sem instalar skill pessoal ou mudar a configuração do Codex. O gate do CI continua sendo o bloqueio verificável de estrutura, complementando as instruções.
