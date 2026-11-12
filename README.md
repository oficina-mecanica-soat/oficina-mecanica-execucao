# Oficina Mecânica — Execução

Serviço de diagnóstico, estoque físico, reservas e execução da Fase 4. Responsável: Gabriel. A arquitetura é definida pelas ADRs 0024–0026 e pela RFC-0007 do repositório da API.

## Estado atual — runtime remoto implantado da F4-04

Serviço .NET 10 com Clean Architecture, healthcheck, validação JWT, testes, Dockerfile, manifests Kubernetes, workload Terraform e CI/CD próprios. Domain mantém `Estoque`/`ItemEstoque` e as regras de reserva; Application separa preparação e cancelamento em Request/Response/Validator/UseCase, com portas tipadas. Infrastructure implementa persistência, unidade de trabalho DynamoDB, outbox e processamento local por arquivos; API configura HTTP e a composição das dependências.

`PrepararExecucao` reserva todo o lote ou persiste uma recusa sem reserva parcial. `CancelarPreparacao` libera o saldo uma vez, inclusive após reiniciar o processamento e reentregar com outro `mensagemId`. Estado, saldos, reservas por OS/item, deduplicação e outbox são confirmados na mesma transação, com comparação de revisões técnicas. A versão de negócio segue a RFC-0007 atualizada no [PR #325](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/pull/325).

O [mapeamento, modelo de dados e limites do incremento](docs/extracao-preparacao.md) detalha as garantias e evidências. O Domain também contém `Servico`, `StatusServico` e as mensagens/testes extraídos da API, preservando criação, início e finalização individuais. A entidade agora participa do ciclo persistido: autorização, início manual de cada serviço, finalização individual e finalização explícita com baixa. Os casos de uso também estão conectados a rotas HTTP locais para Administrador/Mecanico, com consulta do estado e resposta 202 após commit. Ainda não há corte público no Gateway ou integração ponta a ponta com OS/cobrança. DynamoDB, SQS FIFO/DLQ e workload foram implantados pela esteira, com processamento remoto explicitamente habilitado e identidade acadêmica validada no startup. A execução real e seus limites estão na seção seguinte; healthcheck isolado continua sem comprovar acesso a banco ou filas. O worker por arquivos permanece separado e restrito a Development. As seções de incrementos abaixo preservam registros históricos de preparação; expressões como “sem implantação” nesses marcos não substituem este estado atual.

### Evidências reais — 11/11/2026

O [CD development 43124391461, tentativa 2](https://github.com/oficina-mecanica-soat/oficina-mecanica-execucao/actions/runs/43124391461) concluiu plano, imagem, apply e rollout. Os PRs #32 e #33 promoveram para release/main. O runtime remoto ficou ligado, enquanto AWS_DEPLOY_ENABLED voltou a false para bloquear novos deploys; isso não desliga os Pods existentes.

No cenário isolado validacao-f4-04-45deea98-1d54-48a7-bdc9-1e6f715f2cc3, um PrepararExecucao com IDs sintéticos, sem itens de estoque e com um serviço válido foi consumido pelo workload. O registro OS/ATUAL foi persistido na revisão 1, e ExecucaoPreparada foi publicado na fila FIFO da OS. Após recriar somente o Pod, a mesma operação foi reenviada com outro mensagemId: o novo recebimento ficou registrado, e a preparação continuou na revisão 1. A consulta da entrada mostrou zero mensagens visíveis/em voo; são contagens aproximadas do SQS. O resultado artificial identificado foi removido da fila OS, sem purge ou exclusão de mensagens alheias. Os registros sintéticos DynamoDB foram preservados como evidência.

No cenário `validacao-dlq-43b2955e-e7d8-40b3-81ef-84720be2eb59`, foi enviado um envelope sintético com versão não suportada `2`, em grupo FIFO exclusivo. Entrada e DLQ estavam sem pendências antes do teste. O consumidor registrou cinco falhas mantendo a entrada; sem antecipar a visibilidade da entrada ou alterar sua política de redrive pelo operador, o SQS encaminhou a mensagem à DLQ. A leitura na DLQ confirmou o mesmo mensagemId/correlação e informou ApproximateReceiveCount=6 no momento da conferência. Não foi criado registro RECEBIDA para a mensagem inválida. Depois da prova, somente essa mensagem artificial foi removida da DLQ; não houve purge ou redrive manual. A recuperação de uma mensagem corrigida da DLQ não foi testada.

Essa prova não usa a identidade do runner como substituta do workload: o processamento e a publicação foram realizados pela aplicação, que inicia somente após validar conta e LabRole no STS. As consultas de conferência foram feitas pelo operador. A concessão continua sem isolamento IAM por aplicação; IMDSv2 obrigatório/hop limit 2 foi aplicado pela infraestrutura Kubernetes. O ajuste por instância precisa ser conferido após substituição de nodes.

Na renovação, um verificador temporário compilado com os adaptadores existentes executou `ConsumidorSqs` dentro do container com a identidade acadêmica validada. Um recibo real de mensagem sintética foi reservado pelo operador e entregue ao adaptador por recepção controlada; o processamento foi substituído por espera de 25 segundos. A chamada de `ChangeMessageVisibility` e o ACK foram reais e confirmados pelo SQS: uma renovação e uma confirmação para `bbb42501-2791-4a14-8b9f-84c1440e3bce`. A prova cobre o timer do adaptador e essas permissões na entrada; não comprova uma operação de negócio lenta completa nem recepção pelo worker nesse cenário. A recepção pelo workload foi comprovada no cenário inicial. O lote sintético usado para obter isolamento não altera o contrato ou a configuração das filas.

Na recuperação de outbox, o verificador criou uma pendência sintética tipada no DynamoDB real, agendada para 75 segundos depois. O `PublicadorOutbox` existente, com relógio controlado e transporte que lança falha antes de enviar, persistiu uma tentativa e `FALHA_PUBLICACAO`. A asserção inicial do verificador interpretou incorretamente o retorno `false` de falha preservada e encerrou; a execução auxiliar também causou `OOMKilled` no container. Após o reinício automático pelo Kubernetes, o worker real retomou a pendência `validacao-outbox-59848c08-106a-4c98-b44e-2e9bb316beaf`, publicou o mesmo mensagemId e persistiu `OUTBOX#ENVIADA`, removendo `OUTBOX#PENDENTE`. O resultado artificial identificado foi removido da fila OS. Isso comprova retomada durável dessa pendência, não atomicidade de geração por uma operação de negócio ou falha real de rede/SQS; essas etapas não devem ser confundidas.

O verificador e seus arquivos ficaram fora do repositório e temporariamente no container; não há código, hook de falha ou estrutura auxiliar versionados. A tentativa de pausar PID 1 foi recusada pela checagem e não enviou mensagem; o serviço seguiu ativo. O reinício eliminou os arquivos temporários do container. O uso de um segundo processo .NET instrumentado no mesmo container excedeu o limite de memória: não repetir essa forma de carga auxiliar no Pod; planejar isolamento do verificador antes de novos cenários. A compilação temporária apresentou NU1900 por indisponibilidade de auditoria NuGet; isso não é evidência de auditoria de dependências aprovada.

Após as provas controladas, foram removidas somente as mensagens com IDs sintéticos conhecidos da entrada/DLQ e o resultado sintético da OS; não houve purge. As consultas finais indicaram zero mensagens visíveis/em voo na entrada e DLQ, outbox do caso em ENVIADA, serviço 1/1 Running e AWS_DEPLOY_ENABLED=false. Os registros DynamoDB de evidência foram preservados.

Ainda faltam recuperação de mensagem corrigida da DLQ, ciclo com estoque e integração completa da Saga, além da formalização reproduzível dos cenários controlados. Reiniciar um Pod após publicação já concluída não comprova recuperação de outbox pendente; a evidência acima é específica à pendência persistida antes do reinício. Este registro não declara F4-04/F4-10/F4-11 concluídas nem prova ingestão no Datadog.

### Extração de Servico — marco anterior da F4-05

Foram reaproveitados `Servico`, `StatusServico` (valores 1/2/3), quatro mensagens de validação e os dez cenários de teste correspondentes da API. Preservam IDs locais, referência ao catálogo, valor positivo, transições Pendente → EmExecucao → Finalizado e datas UTC. As mudanças são de namespace e de isolamento dos testes: os helpers mínimos ficam no próprio teste, sem copiar o agregado/factory de OS. Não há referência entre projetos Domain dos serviços nem consulta ao catálogo/banco da OS.

A RFC-0007 define `AutorizarExecucao` e os eventos de início/finalização, enquanto o fluxo atual distingue aprovação e ações individuais do mecânico. Gabriel confirmou a preservação do início manual; o esclarecimento foi integrado na API #335. O incremento anterior preservou as regras de `Servico` sem handlers ou eventos. A seção abaixo descreve a conexão posterior dessas regras à persistência e outbox. O monólito permanece com seu fluxo público até o corte coordenado com OS/Gateway.

Validação local desta extração: **190 testes aprovados, zero ignorados**, cobertura consolidada por classe/linha de **96,70%**, restore/build Release sem avisos/erros e formato verificado. Comparação com a origem confirmou equivalência de `Servico`/`StatusServico` exceto namespace/formato e preservação dos cenários originais. O CI deve confirmar os mesmos gates antes do merge.

Nenhuma alteração desta entrega implementa a extração no monólito. Catálogo, clientes, veículos e coordenação da Saga continuam no serviço de OS. Não há acesso ao banco de outro serviço nem compartilhamento de Domain/migrations.

## Ciclo persistido de execução

`PrepararExecucao.servicos` passa a exigir `servicoId` (instância na OS) e `servicoCatalogoId` (referência ao catálogo), conforme alinhamento explícito com Gabriel e PR próprio da RFC-0007. Ambos são preservados no resultado de preparação e no estado persistido. Não há geração de outro ID nem consulta ao catálogo da OS. Os produtores da F4-06 precisam enviar o novo campo antes do corte; mensagens antigas sem ele são rejeitadas. Preparações persistidas sem vínculo de catálogo exigem reconciliação, sem preenchimento automático ou migração destrutiva.

`AutorizarExecucao` v1 persiste a autorização e os serviços pendentes junto da deduplicação, sem iniciar reparo ou publicar evento de início. Sua versão de orçamento é independente da versão da preparação; a Saga de OS continua responsável por validar a associação com o orçamento aprovado. Reentrega idêntica não reativa preparação cancelada nem troca orçamento/Saga. Autorização conflitante, preparação recusada, cancelada, sem serviços ou de outra OS não avançam.

`IniciarExecucaoServicoUseCase`, `FinalizarServicoUseCase` e `FinalizarExecucaoUseCase` seguem Request/Validator/UseCase e resposta tipada, reaproveitando `Servico`, `Estoque.BaixarItens`, unidade de trabalho e outbox. São entradas da Application, reutilizadas pelo HTTP local descrito abaixo, sem novos comandos SQS. Recebem `OperacaoId` estável na repetição, OS/preparação e serviço quando aplicável. A entrada HTTP verifica JWT/perfil e exige esse identificador; o corte público e a integração do emissor permanecem pendentes.

O primeiro início manual confirma `ExecucaoIniciada` na mesma transação do estado. Outros serviços não repetem esse evento. Finalizar individualmente não baixa estoque nem conclui a execução. A finalização explícita exige todos os serviços concluídos, valida cada reserva e confirma, numa transação, baixa, marcadores `Baixada`, estado final, deduplicação e `ExecucaoFinalizada` com UTC. Repetição preserva IDs/datas/eventos e não repete a baixa. O agregado físico `Execucao` reúne essas invariantes sem copiar o agregado/Saga de OS.

Cancelamento ainda pode liberar uma preparação autorizada sem início físico. Após o primeiro início, não libera estoque nem emite compensação fictícia. Cancelamento e início comparam a mesma revisão do documento da preparação, impedindo que os dois confirmem. Falha técnica mantém a operação pendente; não se torna decisão de negócio. Uma execução finalizada mantém a barreira contra nova preparação da mesma OS. Eventos conservam OS/Saga/correlação da autorização e usam a operação manual como `causationId`.

`ProcessadorPreparacao` aceita o novo comando de autorização; sua porta interna admite resultado imediato nulo, pois confirmar o comando não equivale a publicar evento. Consumidores local/SQS continuam confirmando somente depois do commit. O modelo físico da tabela, chaves e limite de 46 itens permanecem; autorização/serviços ficam no documento da preparação. Não cria tabela, índice, fila, SSM ou papel IAM.

O host remoto, Terraform, manifests e workflows não são ativados por este ciclo; `Program.cs` agora conecta o HTTP local descrito abaixo. `AWS_DEPLOY_ENABLED=false` permanece. O merge integra código e executa CI; não implanta recursos. Ainda faltam integração de identidade e corte coordenado com OS/Gateway, observabilidade distribuída, identidade IAM e prova real no laboratório. A F4-05/F4-10 não está concluída apenas por esses testes locais.

Validação local do ciclo: **220 testes aprovados, zero ignorados**, cobertura consolidada por classe/linha de **96,92%**, restore/build Release sem avisos/erros e formato verificado. Inclui disputas com commits sincronizados, falha antes do commit, reserva inconsistente, autorização/reentrega sem evento fictício, retomada persistida e saldo após baixa. DynamoDB Local é real; SQS e identidade AWS não foram validados em laboratório. O CI confirma os gates e sua cobertura consolidada.

## HTTP local — ações do mecânico

Contrato alinhado com Gabriel e registrado em PR próprio da RFC-0007. A base é `/api/v1/execucoes/{ordemServicoId}/preparacoes/{preparacaoId}`. Os sufixos e perfis derivam de `OrdensServicoController` da API; não há alteração de suas rotas públicas atuais.

| Método e sufixo | Uso | Resposta de sucesso |
| --- | --- | --- |
| GET na base | Consultar estado local, inclusive antes de autorizar ou após cancelar | 200 |
| POST `/servicos/{servicoId}/iniciar-execucao` | Iniciar manualmente um serviço autorizado | 202 |
| POST `/servicos/{servicoId}/finalizar` | Finalizar um serviço individual | 202 |
| POST `/finalizar` | Finalizar explicitamente a execução e confirmar a baixa | 202 |

Todos exigem JWT válido com role `Administrador` ou `Mecanico`, como na referência. Cliente, Atendente e outros perfis recebem 403; token ausente/inválido recebe 401. Não há regra nova de vínculo usuário/mecânico: preserva a autorização por perfil atual. Não foi ampliada a lista de emissores/audiences nem alterada a chave configurada. A Lambda continua emitindo JWT de Cliente; isso não autoriza reparos. Configurar/validar a identidade interna correta permanece na F4-09 com Geovanna.

O corpo dos POSTs contém somente `{ "operacaoId": "UUID" }`. Use um UUID não vazio por ação e preserve-o nas repetições; use IDs distintos para iniciar, finalizar serviço e finalizar execução. Os IDs de domínio vêm da rota. Corpo extra, malformado ou inválido retorna 400. A resposta 202 traz `operacaoId`, `resultado` e `Location` para o GET do recurso, somente após commit local. Não significa que a OS recebeu o evento, que houve pagamento ou que a Saga concluiu. Não foi criada rota HTTP de `AutorizarExecucao`.

O GET projeta preparação, versões, indicadores históricos de reserva/cancelamento e ciclo de serviços; não expõe envelope, hash ou credenciais. `reservada` significa que a reserva foi confirmada historicamente, não o saldo atual. Uma preparação cancelada continua consultável, inclusive após preparar outra versão. Não grava estado de negócio; usa leituras consistentes com condições na unidade de trabalho existente.

Erros seguem `ErrorResponse`/`TipoErro` extraídos da API: 404 para preparação ausente ou de outra OS; 409 para conflito de operação/persistência; 422 para regra de negócio; 500 genérico para falha técnica. Não retorna detalhes sensíveis do SDK. No host atual, autenticação/perfil são conferidos antes da persistência e o processamento deve estar habilitado em Development. Caso contrário, identidade autorizada recebe 503 antes de resolver clientes. Essa barreira evita fallback local em produção; não comprova prontidão real do banco e deverá ser adaptada quando a composição remota for integrada/validada. `/api/health` permanece público.

Artefatos locais: [OpenAPI](docs/oficina-mecanica-execucao-openapi.json) e [collection Postman](docs/oficina-mecanica-execucao.postman_collection.json). Importar a collection, preencher `jwt`, IDs e os três IDs de operação. Nenhuma credencial está embutida e não há script de geração de token. Primeiro preparar/autorizar pela mensageria local; a collection não simula autorização da Saga. O Compose habilita o modo local em Development; fora dele, configure o modo explicitamente conforme Execução local.

Validação: **245 testes aprovados, zero ignorados**, cobertura local consolidada de **97,04%**, build Release sem avisos/erros e formato verificado. Requisições HTTP reais no TestServer com JWT assinado e DynamoDB Local comprovam 202/Location após commit, repetição, baixa, consulta/histórico, erros e 401/403. O transporte por arquivos é isolado nesses testes; seus workers continuam cobertos pela suíte existente. Testcontainers/SSH.NET usam as mesmas versões dos testes de infraestrutura. Não houve validação de login real, emissor compartilhado, Gateway, SQS remoto ou IAM.

Gateway, OpenAPI/Postman públicos da solução e retirada/encaminhamento de rotas antigas continuam no corte integrado F4-09/F4-10. Estes artefatos documentam somente o serviço local, sem apresentar o edge como alterado. Terraform, manifests, pipelines e AWS permanecem preservados/desligados. Merge apenas por Gabriel.

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

Resposta esperada: `Healthy`. A porta 5094 evita conflito com a API atual em 5093. O processo recusa inicialização sem issuer, audience e chave válida. O healthcheck é público e não comprova persistência. As rotas manuais e suas condições de disponibilidade estão descritas na seção HTTP local.

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

F4-04/F4-05: validar a identidade AWS do workload, conectar o runtime ao DynamoDB/SQS reais e comprovar leitura/escrita/consumo pelas esteiras na janela autorizada. O ciclo persistido e HTTP local já estão preparados; integrar emissor JWT, produtor OS e corte público de forma coordenada. Atualizar contratos SSM na tarefa de infraestrutura e integrar com OS na F4-10. Observabilidade distribuída e métricas da Saga/DLQ serão evidenciadas na F4-11. O workload atual permanece sem consumidor remoto habilitado; não habilitar deploy para afirmar integração ainda não validada.

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

## Observabilidade — preparação da F4-11

O Execução segue ADR-0021 e RFCs 0006/0007: Datadog nativo e Serilog, sem SDK/exporter OpenTelemetry. Reaproveita o padrão da API (`Serilog.AspNetCore` 10.0.0, JSON e `X-Correlation-Id`); o SDK manual `Datadog.Trace` 3.50.0 acompanha a versão da injeção já usada pela API. O domínio não depende dessas bibliotecas.

O envelope aceita `traceContext` opcional, contendo exclusivamente `traceparent` W3C versão 00 válido. Cabeçalhos extras, baggage, tracestate, credenciais e valores inválidos não são persistidos nem enviados. Ausência do campo mantém a serialização anterior. Esse metadado não participa do hash/idempotência de negócio: uma repetição mantém a identidade e o contexto do resultado já confirmado.

O processador e o publicador retomam o contexto validado; os resultados o persistem atomicamente na outbox para sobreviver a reinícios. Ações manuais capturam o contexto nativo ativo ou preservam o `traceparent` HTTP validado, com fallback para o contexto de origem. `X-Correlation-Id` continua sendo correlação funcional independente; a resposta HTTP o devolve, preservando valor sem controles de até 128 caracteres ou gerando UUID. Não muda payload, perfil ou regra de início manual.

Logs JSON incluem serviço/ambiente/versão, operação, correlação e duração. HTTP registra o template da rota e remove propriedades de caminho bruto do escopo ASP.NET; não registra query, corpo, Authorization ou detalhes de exceções. `dd.trace_id`/`dd.span_id` só aparecem quando fornecidos pelo tracer real. Sem profiler, os testes comprovam metadados e persistência, não exportação APM; o SDK manual sozinho não instala o profiler. Logs de processamento/publicação representam tentativas e não contadores únicos de negócio.

No futuro apply autorizado, o Deployment existente receberá tags `env/service/version`, habilitação da admissão, anotação do container existente `execucao` e biblioteca .NET 3.50.0. `DD_VERSION` usa o SHA da imagem; injeção/extração usam `tracecontext` e coleta de query HTTP fica desabilitada. O manifesto local descreve a mesma configuração com versão `local`. Não cria Agent, recurso AWS, IAM ou parâmetro SSM. Não altera pipelines nem ativa o consumidor remoto.

Antes do corte integrado, produtores/consumidores precisam aceitar o campo opcional aprovado na RFC; deserializadores estritos antigos exigem atualização coordenada. Injeção real, conectividade com Agent, correlação de spans/logs, consumo de memória, métricas, painéis e alertas continuam pendentes da janela autorizada e das demais frentes. `AWS_DEPLOY_ENABLED=false` permanece preservado. Avisar Gabriel antes de precisar do laboratório; nenhum deploy/apply/destroy faz parte desta entrega.

Validação deste incremento: restore/build Release sem avisos, 259 testes aprovados e nenhum ignorado, cobertura local consolidada por classe/linha de 96,66% (1244/1287). Formato .NET, gate de estrutura dos dois repositórios e cinco testes Terraform com providers simulados aprovados. O CI do PR é a evidência final do gate de cobertura. Os testes locais não executaram profiler/Agent Datadog nem AWS.

## Integração local das entradas — preparação da F4-10

Os cenários BDD de `ExecucoesHttpTests` passam pelos adaptadores `ConsumidorSqs`/`TransporteSqs`, HTTP/JWT do mecânico e DynamoDB Local na mesma jornada. Reaproveitam o host e a fixture existentes; `Moq` 4.20.72 segue a versão dos demais projetos de teste. Não há implementação alternativa de Saga, contrato compartilhado ou novo runtime.

| Cenário | Evidência esperada |
| --- | --- |
| Preparar/autorizar por SQS, iniciar/finalizar por HTTP e recriar host | Autorização não inicia serviços; repetição preserva datas; baixa única; outbox retomada publica preparação/início/fim com IDs FIFO e correlação preservados. |
| Cancelar com falha de ACK depois do commit e recriar host | Estado/saldo já confirmados antes do ACK; reentrega do mesmo envelope ou com novo mensagemId não libera duas vezes nem duplica resultado; início posterior bloqueado. |
| Preparar sem saldo e repetir após recriar host | Sem reserva parcial; um resultado de recusa; autorização ausente e saldo inalterado. |

Os testes recriam a aplicação e seus serviços, mantendo a mesma tabela no container; não simulam reinício do DynamoDB nem durabilidade AWS. O cliente SQS é simulado: inspeciona chamadas, corpos e metadados, sem reproduzir o broker FIFO/DLQ. Os comandos são dados de teste conforme RFC-0007; OS, Orçamento/Pagamento, Mercado Pago, Gateway e emissor JWT real não participam. Não constitui prova ponta a ponta dos três serviços nem conclusão da F4-10.

Nenhum código de produção, recurso, contrato, manifesto ou workflow muda neste incremento. Os testes entram no job existente e preservam o gate de 90%. AWS permanece bloqueada; identidade IAM, ativação remota e validações reais aguardam alinhamento/janela autorizada.

Validação local deste incremento (11/11/2026): restore/build Release sem avisos/erros, formato verificado, 262 testes aprovados e nenhum ignorado; cobertura consolidada de 96,81% (1246/1287). Gate de estrutura aprovado, sem arquivos ou diretórios adicionados. O CI do PR confirma a evidência final do gate.

## Identidade do workload — preparação offline da F4-04

Levantamento em 11/11/2026 conforme ADR-0025. O repositório Kubernetes referencia roles existentes para o cluster e os nodes (`modules/eks/data.tf` e `node-group.tf`), com nomes parametrizados pelo laboratório. Não foi encontrada configuração de IRSA ou EKS Pod Identity na plataforma atual. Isso não comprova qual identidade um Pod obterá nem as permissões efetivas dessa identidade. Credenciais do runner usadas nas esteiras não constituem identidade da aplicação.

### Permissões derivadas dos adaptadores atuais

Esta matriz é requisito para revisão da futura política, não uma concessão aplicada nem autorização para alterar a role dos nodes. Os ARNs concretos devem vir dos recursos/contratos dos respectivos owners no ambiente autorizado.

| Componente e chamada existente | Ação IAM necessária | Escopo do recurso |
| --- | --- | --- |
| `ArmazenamentoDynamoDb.GetItemAsync` | `dynamodb:GetItem` | Somente tabela do Execução. |
| `ArmazenamentoDynamoDb.QueryAsync` (outbox) | `dynamodb:Query` | Mesma tabela; não há índice nessa consulta. |
| `TransactWriteItemsAsync`, operações Put/Delete/ConditionCheck | `dynamodb:PutItem`, `dynamodb:DeleteItem`, `dynamodb:ConditionCheckItem` | Mesma tabela; avaliar condição `dynamodb:EnclosingOperation=TransactWriteItems` para restringir essas escritas à transação. |
| `ConsumidorSqs.ReceiveMessageAsync` | `sqs:ReceiveMessage` | Somente entrada FIFO do Execução. |
| `ConsumidorSqs.DeleteMessageAsync` | `sqs:DeleteMessage` | Mesma entrada, depois do commit. |
| `ConsumidorSqs.ChangeMessageVisibilityAsync` | `sqs:ChangeMessageVisibility` | Mesma entrada, para renovação. |
| `TransporteSqs.SendMessageAsync` | `sqs:SendMessage` | Somente entrada FIFO de resultados da OS, publicada pelo owner API. |

O IAM do DynamoDB autoriza os componentes da transação; não inventar uma ação `dynamodb:TransactWriteItems` como substituição. O código atual não chama UpdateItem. Não requer Scan, criação/remoção de tabela/filas, purge, redrive ou consumo da DLQ. O redrive à DLQ é responsabilidade do SQS/configuração de infraestrutura. As permissões de provisionamento, verificação de deploy e operação controlada da DLQ pertencem à esteira, separadas deste mínimo do runtime.

Os adaptadores recebem tabela/URLs e clientes prontos. Não consultam SSM/Secrets Manager, IAM ou STS para cada operação; não acrescentar essas permissões por suposição. A futura inicialização do host e o mecanismo de obtenção de credenciais podem exigir permissões próprias, a detalhar após a decisão de identidade. O runtime não deve receber permissão no banco da OS/cobrança. A fila de OS aceita somente publicação de resultados por este serviço.

### Decisão e validação ainda necessárias

O levantamento inicial deixou o mecanismo condicionado ao laboratório. Em 11/11/2026, Gabriel aprovou a exceção acadêmica integrada na [API #347](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/pull/347): identidade existente dos nodes (`LabRole`) para o Execução, sem isolamento IAM por aplicação. Não autoriza alteração de roles/trust ou extensão automática aos demais serviços. IRSA/Pod Identity permanecem evolução para ambiente com capacidade apropriada. Não transportar credenciais do runner para o Pod nem aceitar fallback silencioso.

Antes de implementar a integração remota:

1. Na janela autorizada, verificar as roles disponíveis e a capacidade de configurar trust/políticas/associação; registrar impedimentos sem expor credenciais. Acesso negado não equivale a recurso ausente. Conferir os controles de acesso a metadados dos nodes e a resolução real de credenciais pelo SDK.
2. Alinhar com Gabriel o mecanismo e o isolamento viáveis, com impacto sobre a plataforma compartilhada. Registrar na arquitetura antes do PR de implementação. Não escolher/criar nomes de service account, role ou novos contratos SSM neste levantamento.
3. Preparar PRs nos owners corretos e integrar o host explicitamente com os clientes aprovados; preservar a indisponibilidade remota atual até essa implementação. Não alterar os modos local/remoto por este documento.
4. Pelas esteiras existentes, validar a identidade dentro do workload, acesso à tabela própria, consumo/renovação/ACK da entrada e publicação à OS, com dados de teste isolados. Testar limites de acesso quando viável; sucesso em um recurso não comprova isolamento dos demais.
5. Comprovar reinício/outbox, duplicata, FIFO/DLQ e encerramento; preservar as mensagens/dados da demonstração e os guardrails de teardown. Registrar somente evidências não sensíveis. O SDK simulado/DynamoDB Local não substitui essa prova.

**Estado após a decisão:** mecanismo acadêmico aprovado na ADR-0025; prova dentro do Pod ainda pendente. O laboratório foi disponibilizado para consultas de leitura, sem autorização de deploy/apply/destroy. O componente de clientes abaixo prepara a implementação sem ativar o host. `AWS_DEPLOY_ENABLED=false` permanece; recursos, secrets, políticas e workflows não foram alterados.

Referências primárias: [IAM em transações DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/transaction-apis-iam.html), [permissões de ações SQS](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-api-permissions-reference.html) e [identidade e acesso no EKS](https://docs.aws.amazon.com/eks/latest/best-practices/identity-and-access-management.html). As referências descrevem capacidades AWS, não comprovam disponibilidade no Academy.

## Clientes AWS com identidade acadêmica — preparação sem ativação

`ClientesAwsAcademicos.CriarAsync(regiao, contaEsperada, ct)` utiliza explicitamente `InstanceProfileAWSCredentials("LabRole")`, sem cadeia de credenciais por environment/perfil. Antes de construir os clientes DynamoDB/SQS, consulta `GetCallerIdentity` no STS com essa mesma fonte e exige resposta confirmada, conta esperada e ARN de sessão `assumed-role/LabRole/...`. Rejeita usuário IAM, role de runner (`voclabs`), outra conta/role, ARN incompleto e resposta sem confirmação. A conta esperada é parâmetro obrigatório independente da identidade observada; não é derivada da própria resposta que se pretende validar.

Credenciais ausentes, falha técnica ou cancelamento interrompem a criação; não retornam clientes por fallback. O SDK mantém a renovação das credenciais do instance profile. Não registra nem persiste tokens/chaves. O objeto retornado é dono dos clientes/credenciais e deve viver até o encerramento do host: chamar `AddInfrastructureRemota` com seus clientes não transfere esse descarte à composição existente. A dependência `AWSSDK.SecurityToken` 4.0.101.6 segue a família v4 dos adaptadores atuais; Domain/Application permanecem sem dependência de autenticação AWS.

No incremento original dos clientes, o componente não era chamado pelo Program. A integração posterior descrita abaixo o conecta somente quando o modo remoto é explicitamente habilitado; sem isso, a barreira local e os 503 anteriores permanecem. Não há nova variável, ARN de conta real versionado, acesso SSM no runtime ou alteração de IAM, metadados dos nodes, Terraform, manifests ou pipelines. O próximo incremento deverá conectar o host/configuração e a disponibilidade remota explicitamente, preservando autenticação e validações.

Os testes usam STS simulado e verificam aceitação/rejeição de identidade, propagação de falhas/cancelamento e configuração inválida antes de obter credenciais. Não consultam metadados EC2 ou STS reais e não comprovam IMDS acessível, renovação no Pod ou permissões de banco/filas. A construção bem-sucedida real dos clientes e seu descarte na integração do host permanecem para validação posterior. A prova acadêmica exige implantação autorizada e acesso real à tabela/fila próprias e publicação à OS. A concessão continua sem isolamento IAM entre aplicações com acesso às credenciais dos nodes.

Referência de implementação: [InstanceProfileAWSCredentials no AWS SDK .NET v4](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/Runtime/TInstanceProfileAWSCredentials.html), que distingue essa fonte explícita do provider usado na cadeia de fallback.

Validação local deste incremento: 280 testes aprovados, nenhum ignorado, cobertura consolidada 95,17% (1260/1324), restore/build Release sem avisos e formato verificado. Gate de estrutura e diff aprovados. Caminhos de IMDS/construção real dos clientes não foram executados para evitar autenticação real nos testes; não foram excluídos da cobertura. O gate do CI permanece em 90%.

## Composição do host — modo remoto opt-in

Após Execução #23, `Program` escolhe uma única composição antes de construir/iniciar a aplicação. Sem `ProcessamentoRemoto:Habilitado=true`, mantém a composição local anterior, sem obter credenciais AWS. Se os modos local e remoto estiverem habilitados juntos, falha antes da autenticação. Com remoto habilitado, exige todos os parâmetros abaixo, verifica a conta e as URLs antes de obter credenciais e aguarda a validação STS da identidade acadêmica. Só então registra `AddInfrastructureRemota` com o worker e os clientes existentes. Falha não volta ao modo local.

| Chave de configuração (environment usa `__`) | Valor/origem na futura implantação |
| --- | --- |
| `ProcessamentoRemoto:Habilitado` | Ausente/false por padrão; true somente na janela autorizada de validação/ativação. |
| `ProcessamentoRemoto:ContaEsperada` | Conta de destino conferida independentemente da resposta STS do Pod. |
| `ProcessamentoRemoto:Regiao` | Região dos recursos; sem fallback de região no host. |
| `ProcessamentoRemoto:Tabela` | Contrato existente `execucao/dynamodb_table_name`. |
| `ProcessamentoRemoto:FilaEntradaUrl` | Contrato existente `execucao/entrada_queue_url`. |
| `ProcessamentoRemoto:FilaResultadosOsUrl` | Contrato existente `api/entrada_queue_url`, do owner OS. |

Essas chaves são configuração do runtime, não novos parâmetros SSM. As duas URLs devem ser HTTPS/FIFO válidas, distintas, do endpoint SQS da conta/região configuradas e sem porta alternativa. Configuração incompleta ou destino divergente bloqueia antes do STS. O host não consulta SSM nem cria tabela/filas. O incremento do host não acrescentou parâmetros ao workload; a configuração pela esteira é descrita na seção seguinte.

O objeto `ClientesAwsAcademicos` é mantido vivo ao redor de `RunAsync` e descartado ao encerrar/sair do host; a DI recebe seus clientes sem assumir sua propriedade. O filtro HTTP reconhece o modo efetivamente composto após o STS, preservando JWT/perfis e casos de uso. No modo local, continua exigindo Development e processamento local habilitado, retornando 503 nos demais casos. Nenhum endpoint HTTP, body, evento ou política de acesso foi criado.

Testes locais verificam configuração recusada antes de autenticar, exclusão entre modos, composição local padrão e disponibilidade do filtro por modo, além das regressões HTTP/persistência existentes. Não exercitam startup remoto bem-sucedido com IMDS/STS reais, descarte real dos clientes no Pod ou acesso DynamoDB/SQS do workload. STS confirma identidade, não permissões de banco/filas. Validação dessas permissões, renovação, FIFO/DLQ/outbox e ingestão Datadog continua obrigatória na janela autorizada antes de apresentar o remoto como operacional.

`AWS_DEPLOY_ENABLED=false` permanece; sem alteração de workflows, manifests, Terraform, IAM ou exposição Gateway. O PR integra código apto à configuração explícita, sem habilitar o workload atual. A integração pública e os produtores dos outros serviços continuam nas frentes acordadas.

Validação local: 299 testes aprovados, nenhum ignorado; cobertura consolidada 94,90% (1285/1354). Restore/build Release sem avisos e formato verificado. O gate de 90% permanece; caminhos de startup remoto real não foram executados nem excluídos. Diff/gate de estrutura e CI do PR devem ser conferidos antes do merge.

## Configuração remota do workload pela esteira — F4-04

### Recuperação do provider após timeout de criação

Após corrigir IMDSv2 na infraestrutura Kubernetes, o Pod iniciou, mas o novo plano falhou em `kubernetes_deployment_v1.execucao` com `Unexpected Identity Change`: identidade anterior com campos nulos e identidade real preenchida na leitura. O primeiro apply havia criado o Deployment e terminado em timeout de rollout; o state parcial foi preservado pela esteira.

O provider Kubernetes fica fixado em `3.2.1`, que inclui a correção de identidade após recursos demorarem a ficar prontos, conforme [changelog oficial da HashiCorp](https://github.com/hashicorp/terraform-provider-kubernetes/blob/main/CHANGELOG.md#321-jun-30-2026). O lockfile acompanha essa seleção; Terraform 1.15.8, provider AWS, recursos `*_v1`, workflows e contratos permanecem os mesmos. A configuração e os nove testes Terraform com providers simulados passam, mas a recuperação do state real exige a próxima execução autorizada após merge.

Não remover, importar, editar ou descartar state como atalho. O próximo plano deverá restaurar o cache da conta, usar o provider corrigido e evidenciar as ações propostas, inclusive eventual substituição do Deployment marcado pela falha anterior. Não há autorização para apagar tabela ou filas; healthcheck do Pod não comprova identidade/permissões DynamoDB/SQS nem encerra a F4-04. Este PR não executa apply.

### Preparação do próximo apply

O controle versionado retorna para `TERRAFORM_ACTION=apply` após o bootstrap de state da conta do laboratório. `AWS_DEPLOY_ENABLED=false` e o processamento remoto desligado permanecem até janela específica. Este preparo não executa deploy nem modifica o host, contratos ou recursos declarados.

O futuro apply utilizará a esteira existente: validação da plataforma/registry, restauração e conferência do state por conta/ambiente, plano salvo por tentativa, publicação da imagem aprovada na CI, aplicação desse mesmo plano, verificações reais de dados/mensageria e rollout. Não altera os workflows, quality gates, promoções ou guardrails de destroy.

Serão provisionados os recursos já declarados do Execução: tabela DynamoDB, entrada FIFO/DLQ e redrive, contratos SSM e workload Kubernetes próprio (namespace, configuração, Secret, Deployment e Service). Consome EKS/ECR compartilhados, sem recriar rede, cluster, RDS ou recursos da OS. Não cria role IAM nem copia credenciais do runner para o Pod.

Para a validação remota da F4-04, a janela deverá incluir habilitação explícita de `PROCESSAMENTO_REMOTO_HABILITADO`, exigindo o contrato real da fila da OS e identidade `LabRole` validada no Pod, conforme ADR-0025. A aprovação deste PR não habilita esse controle. Health/rollout não comprovam acesso DynamoDB/SQS: identidade, leitura/escrita, consumo/renovação/ACK, publicação, duplicatas e recuperação continuam pendentes de evidência real. A integração completa da Saga permanece na F4-10 com as frentes responsáveis.

Após Execução #24/API #350, o Terraform fornece ao Deployment os parâmetros do host remoto. O controle `PROCESSAMENTO_REMOTO_HABILITADO` é uma GitHub Variable usada pelo job existente de preparação do Environment: ausente/`false` mantém remoto desligado; somente `true` solicita ativação na futura aplicação do plano. Outro valor bloqueia apply antes de consultar contratos. Não é credencial ou novo contrato SSM. `AWS_DEPLOY_ENABLED` continua sendo a barreira de deploy, independente desse controle. Alterar variables não executa deploy por si só; a execução segue os triggers e guardrails existentes.

Com remoto solicitado, a preparação exige `/oficina-mecanica/{ambiente}/status/api=ready`, lê os contratos existentes `api/entrada_queue_url` e `api/entrada_queue_arn` e compara com o nome aprovado `oficina-mecanica-api-{ambiente}.fifo`, a conta autenticada e a região. Consulta o ARN real via GetQueueAttributes antes de gerar tfvars/plano. Ausência, divergência ou falha de acesso bloqueiam; não usa URL manual, outra fila ou conta por fallback. O marcador é de infraestrutura de mensageria, não prova de saúde da OS/Saga.

Tabela e entrada próprias são referências diretas aos recursos DynamoDB/SQS do Terraform, os mesmos valores publicados nos contratos `execucao/*`. Não busca esses parâmetros no SSM antes de criá-los: isso permite bootstrap na primeira aplicação. Conta esperada vem do data source de identidade do Terraform, independente da identidade que o Pod verificará no STS; região vem do ARN da fila própria. A precondição do Deployment também bloqueia destino de OS ausente ou incompatível. Essas verificações não alteram o ownership dos recursos.

Desligado por padrão, o Deployment recebe `ProcessamentoRemoto__Habilitado=false` e `ProcessamentoLocal__Habilitado=false`. Ligado explicitamente, recebe também conta, região, tabela e URLs aprovadas. Não recebe access key, secret key ou session token do runner. O manifesto local de referência não recebe ativação AWS. Não cria nova role/service account, abre IMDS, altera rede ou configura Gateway.

O controle é convertido em tfvars antes de gerar o plano e fica incorporado ao artefato já usado pelo apply. Não há novo plano no job de aplicação, reconstrução de imagem ou fluxo alternativo. Destroy ignora o controle remoto e não depende de SSM/saúde de OS para preparar os valores; mantém os guardrails de state, outbox/filas, plano salvo e recuperação já existentes. Nenhuma proteção foi removida.

O diff altera configuração do Deployment existente e as validações da preparação. Em laboratório vazio, o futuro apply também criará os recursos **já declarados anteriormente** (tabela, entrada/DLQ/SSM e recursos Kubernetes do serviço), dependendo da plataforma compartilhada. Não são recursos novos desta mudança. Com remoto ativado, a infraestrutura de mensageria da OS deverá existir antes desse apply; pode-se preparar recursos próprios com remoto desligado quando necessário, sem anunciar funcionamento do serviço distribuído.

Validação local: Terraform fmt/validate e nove cenários com providers simulados aprovados; 16 testes inline do Execução e os nove da referência API aprovados, além de sintaxe Node e actionlint (sem shellcheck/pyflakes locais). Os testes cobrem padrão desligado, recursos próprios, destino incompatível/ausente, contrato/ARN real divergentes, acesso negado e independência de destroy. Não foi alterado código .NET; a suíte será observada no CI do PR. Não são prova de IAM, IMDS/STS, consumo/escrita ou rollout reais.

**Ainda sem implantação:** `AWS_DEPLOY_ENABLED=false` permanece e o controle remoto não foi ligado no GitHub. Depois do merge de Gabriel, preparar a janela autorizada e conferir credenciais/dependências antes de habilitar qualquer execução. Pod que não obtiver a LabRole validada falhará no startup; HTTP health não comprova permissões nos dados/filas. F4-04 e observabilidade real permanecem pendentes das evidências no laboratório.
