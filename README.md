# Oficina Mecânica — Execução

Serviço de diagnóstico, estoque físico, reservas e execução da Fase 4. Responsável: Gabriel. A arquitetura é definida pelas ADRs 0024–0026 e pela RFC-0007 do repositório da API.

## Estado desta entrega — preparação e cancelamento da F4-05

Serviço .NET 10 com Clean Architecture, healthcheck, validação JWT, testes, Dockerfile, manifests Kubernetes, workload Terraform e CI/CD próprios. Domain mantém as regras de estoque/reserva; Application processa os contratos de preparação/cancelamento; Infrastructure persiste transações no DynamoDB e fornece transporte substituível por arquivos para validação local.

`PrepararExecucao` reserva todo o lote ou persiste uma recusa sem reserva parcial. `CancelarPreparacao` libera o saldo uma vez, inclusive após reiniciar o processamento e reentregar com outro `mensagemId`. Estado, saldos, reservas por OS/item, deduplicação e outbox são confirmados na mesma transação, com comparação de revisões técnicas. A versão de negócio segue a RFC-0007 atualizada no [PR #325](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/pull/325).

O [mapeamento, modelo de dados e limites do incremento](docs/extracao-preparacao.md) detalha as garantias e evidências. `AutorizarExecucao`, início/finalização física e integração ponta a ponta com OS/cobrança permanecem pendentes. DynamoDB, SQS FIFO e DLQ reais pertencem à F4-04 e ainda não estão provisionados nem conectados. O healthcheck verifica somente a aplicação; não comprova acesso a banco ou filas. O worker por arquivos só pode ser habilitado em Development e não é consumidor SQS.

Nenhuma alteração desta entrega implementa a extração no monólito. Catálogo, clientes, veículos e coordenação da Saga continuam no serviço de OS. Não há acesso ao banco de outro serviço nem compartilhamento de Domain/migrations.

## Fontes da verdade

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

Para uma demonstração automatizada de reserva, dois reinícios reais da API, reentrega e cancelamento, com evidências locais preservadas:

```powershell
# Requer também AWS CLI instalada; o script usa exclusivamente endpoint localhost.
./scripts/validar-fluxo-local.ps1
```

O script semeia um item novo no DynamoDB Local, confere saldo `10 → 7 disponíveis/3 reservados → 10/0`, e encerra os containers no final. Não usa nem modifica credenciais remotas. `-ManterContainers` mantém o ambiente local para inspeção. Arquivos substituem apenas o transporte; esses testes não comprovam FIFO, DLQ, IAM, visibility timeout ou conectividade SQS real.

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
node --test .github/scripts/*.test.cjs
```

CI deriva da API: escopo documental, Git Flow, build, formato, testes sem ignorados, cobertura mínima de **90%** preservada da referência (acima dos 80% mínimos da Fase 4), Docker, manifests em Kind e Terraform. Analisadores .NET e avisos como erros fazem parte do build; a análise Sonar/equivalente exigida na F4-12 deve ser comprovada antes do aceite final. Scripts e testes de recuperação de destroy são preservados da API; não constituem evidência de recuperação de um deploy deste serviço ainda inexistente.

Validação local em 10/11/2026: build sem avisos/erros; formato verificado; **11 testes .NET aprovados, zero ignorados e 100% de cobertura de linhas da base atual**; quatro testes Node de guardrails aprovados; Terraform 1.15.8 validado; imagem Docker construída e `GET /api/health` retornou `200 Healthy` em `localhost:5094`. Container e rede Compose encerrados após o teste. Esses números cobrem somente a base da F4-03, sem regras de estoque ou execução implementadas.

A evidência acima é o marco histórico da F4-03. A validação da preparação/cancelamento e seus limites está em [extracao-preparacao.md](docs/extracao-preparacao.md). Testes de integração exigem Docker e falham se o container não puder iniciar; não são ignorados. Testcontainers usa a versão de referência do projeto com SSH.NET 2026.0.0 fixado somente no projeto de testes, corrigindo a dependência transitiva vulnerável sem suprimir os avisos do build.

## CI/CD e infraestrutura

Fluxo: `feature/* → PR develop → PR release → PR main`. As branches de estágio não recebem código diretamente. Promoções usam merge commit para preservar ancestralidade. Workflows próprios: CI, CD Development, AWS Deploy reutilizável, CD Release e CD Production. Homologation/production continuam promoções lógicas.

**AWS permanece desligada.** `AWS_DEPLOY_ENABLED=false` impede chamar o deploy no merge de código. Um push deployável sem execução AWS também não promove automaticamente para release. Alterações exclusivamente documentais preservam o fluxo de promoção sem AWS. Não habilitar o deploy nem trocar o controle operacional para provocar provisionamento sem autorização específica.

O workload Terraform possui namespace próprio `oficina-mecanica-execucao`, Deployment, Service privado e Secret JWT, sem criar VPC, EKS, registry compartilhado ou banco alheio. Manifests `k8s/` são referência local/dry-run; na AWS o ownership é exclusivo do Terraform. O roteamento público será integrado na F4-09.

Dependências antes do primeiro deploy autorizado:

1. Infra Kubernetes deve criar o registry **oficina-mecanica-execucao**, conforme ADR-0023/25. Não usar o registry da API nem criar ECR neste serviço.
2. Plataforma publica os contratos existentes `/oficina-mecanica/development/status/kubernetes` e `/oficina-mecanica/development/kubernetes/cluster_name`.
3. Configurar Environment `development` com secrets `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`, `JWT_SECRET`; variables `AWS_REGION`, `ECR_REPOSITORY_NAME`, `JWT_ISSUER`, `JWT_AUDIENCE`.
4. Confirmar emissor/audience compatíveis com os consumidores; configurar a chave sem divulgar/copiar valores em documentação.
5. Primeiro state exige `ALLOW_INITIAL_STATE_BOOTSTRAP=true` **somente** após confirmar ausência do namespace; desabilitar após o primeiro deploy. State ausente com namespace existente bloqueia a operação.
6. Habilitar `AWS_DEPLOY_ENABLED` apenas na janela autorizada de validação. Nenhum recurso foi criado pela preparação do repositório.

O state local fica no cache do Actions isolado por serviço, conta e ambiente, seguindo a concessão acadêmica existente. Não há fallback entre contas. O Secret Kubernetes contém a chave no state, como na API atual; `sensitive` oculta a apresentação, sem remover o valor do state. A evolução para runtime/Secrets Manager permanece no backlog aprovado.

Terraform 1.15.8:

```powershell
terraform fmt -check -recursive infra/terraform
terraform -chdir=infra/terraform/environments/dev init -backend=false -input=false
terraform -chdir=infra/terraform/environments/dev validate
```

Apply/destroy reais só pela esteira. `terraform-action.env` define a ação. Destroy exige alteração desse arquivo no próprio push, restauração do state, plataforma acessível, plano salvo exclusivo de remoção e validação final de state vazio. Reset do laboratório com EKS ausente exige reconciliação específica antes de usar o deploy: esta base bloqueia em vez de descartar state. Persistir state também após falha parcial evita perder ownership. Respeitar filas/outbox pendentes antes de destruir quando forem implementadas.

Variables do repositório: `AWS_DEPLOY_ENABLED=false`, `AUTO_PR_ENABLED=true`, `RELEASE_BRANCH=release`. Secrets não são provisionados pelo bootstrap. O usuário `soat-architecture` deve ter acesso verificável antes da entrega; repositório público permite leitura, sem afirmar concessão administrativa.

## Próximo passo

F4-04/F4-05: provisionar e validar DynamoDB/SQS FIFO/DLQ pelas esteiras quando houver janela autorizada, conectar o workload ao transporte real e implementar autorização/início/finalização física. Atualizar contratos SSM na tarefa de infraestrutura e integrar com OS na F4-10. Observabilidade distribuída e métricas da Saga/DLQ serão evidenciadas na F4-11. O workload atual permanece sem consumidor remoto habilitado; não habilitar deploy para afirmar integração ainda não validada.
