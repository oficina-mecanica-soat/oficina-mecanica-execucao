# Plano de execução da Fase 4 — Gabriel e integrações com Geovanna

Revisão em 10/11/2026. Este plano detalha a execução do backlog existente e a exigência de uniformidade das esteiras. Não substitui decisões arquiteturais nem declara implementadas as etapas futuras. Merges são realizados por Gabriel; recursos AWS somente pelas esteiras, após aviso para criação do laboratório e atualização dos environments.

## Fontes e nomes dos componentes

Fontes canônicas no repositório `oficina-mecanica-api`: README principal, `docs/projeto/backlog-tecnico.md`, ADR-0023/24/25/26 e RFC-0003/04/05/07. A RFC-0007 inclui o ajuste aprovado no PR #325. READMEs próprios detalham o funcionamento de cada componente. Quando README/guia e implementação divergem, registrar e corrigir a divergência por PR; não criar uma terceira variante nem assumir equivalência.

Contexto original conferido no projeto Codex **FIAP FASE 4**, chat **Localizar contexto do projeto**: anexo `Contexto para IA.zip`, preservado em `D:\Workspace\oficina-mecanica\contexto-projeto`. Foram consultados `prompt_projeto.md`, `estrutura_projeto.md`, Domain Storytelling completo, linguagem ubíqua, diagramas táticos de Estoque/OS e o enunciado da Fase 4 contido no ZIP. Os snapshots de repositórios do arquivo são referência histórica; o estado atual foi conferido nos repositórios remotos.

As restrições antigas de implantação monolítica são sucedidas pela ADR-0024. Permanecem válidos domínio, linguagem, organização por casos de uso, separação de camadas e simplicidade. Não restaurar a topologia antiga nem descartar as convenções do ZIP ao extrair serviços.

| Componente/repositório documentado | Responsabilidade e dados |
| --- | --- |
| `oficina-mecanica-api` | Serviço de OS: clientes, veículos, autenticação interna, administrativo, catálogo, OS e orquestração da Saga; SQL Server existente. |
| `oficina-mecanica-execucao` | Diagnóstico, estoque físico, reserva/liberação, fila e execução; DynamoDB próprio, fila SQS FIFO de entrada e DLQ próprias. Frente de Gabriel. |
| `oficina-mecanica-orcamento-pagamento` | Orçamentos versionados, decisão, cobrança e Mercado Pago; banco lógico SQL Server, credencial e migrations próprios; fila/DLQ próprias. Frente de Geovanna; repositório ainda não encontrado na organização na revisão. |
| `oficina-mecanica-infra-vpc` | Rede compartilhada e contratos SSM da VPC. |
| `oficina-mecanica-infra-kubernetes` | EKS, ECRs dos serviços, NLB e instalação Datadog; infraestrutura compartilhada. |
| `oficina-mecanica-infra-rds` | RDS e contratos de banco separados por serviço; comprovar credenciais isoladas antes da integração. |
| `oficina-mecanica-auth-lambda` | Contrato público de autenticação preservado; migração da consulta SQL para endpoint privado autenticado de OS. |
| `oficina-mecanica-infra-api-gateway` | Entrada pública única, OpenAPI do edge, VPC Link e roteamento aos destinos privados. |

Nenhum novo serviço de cadastro, framework de eventos compartilhado ou SNS está previsto. Cada consumidor é dono da fila de entrada/DLQ; a fila de OS recebe resultados. Não renomear repositórios, mensagens, rotas, claims ou contratos SSM existentes. Onde ainda não houver nome físico de tabela/fila/parâmetro definido, a proposta deve aparecer no PR de infraestrutura/contrato antes do provisionamento; nomes locais de teste não são nomes aprovados de produção.

## Padrão único de esteiras

Referência efetivamente inspecionada: workflows atuais de API, VPC e Kubernetes, comparados com Lambda, RDS, Gateway e Execução. A RFC-0003 prevê jobs técnicos conforme .NET, Terraform ou Lambda; o modelo de governança, promoção e operação é o mesmo. Não criar um fluxo alternativo por serviço nem uma plataforma nova de templates.

| Parte | Regra aplicável à solução |
| --- | --- |
| Git Flow | Branch de trabalho → PR `develop` → CD Development → PR `release` → CD Release → PR `main` → CD Production; checks e revisão preservados. Promoções preservam ancestralidade. |
| Workflows | `ci.yml`, `cd-development.yml`, `aws-deploy.yml`, `cd-release.yml`, `cd-production.yml`, com nomes exibidos e estrutura da referência correspondente. Datadog conserva o workflow especializado existente em Kubernetes. |
| CI | Detecção de escopo; validação de branches; build/formato/testes/cobertura e imagem para aplicações; Terraform/manifests conforme o componente; Quality gate obrigatório com o nome exato configurado no ruleset. Não reduzir gates existentes de 90% ao mínimo acadêmico de 80%. |
| Planejamento | Resolver ação versionada → preparar variáveis/validar dependências e state → gerar plano salvo e publicar artefato → aplicar exatamente esse plano → verificar resultado. |
| Imagem .NET no EKS | Seguir API: publicar a imagem produzida pela CI após conferir a árvore de código validada, em vez de reconstruir outra no CD; tag imutável `sha-*`. |
| State e recuperação | Mesmo mecanismo de cache isolado por conta/ambiente, preservação após falha parcial, bloqueio com state ausente e recuperação documentada. Sem fallback entre contas nem descarte silencioso de state. |
| Apply/destroy | Controle em `infra/terraform/environments/dev/terraform-action.env`; destroy dedicado, plano sem criação/atualização e confirmação de remoção. Publicar/consumir contratos via SSM; material sensível conforme Secrets Manager e concessões acadêmicas vigentes. |
| Promoção | Deploy real de development apenas para alteração implantável; alteração documental segue promoção lógica. Código sem deploy confirmado não é apresentado como implantado. Release/production permanecem ambientes lógicos. |
| Execução | Rollout, health checks, requests/limits, escalabilidade e instrumentação seguem os mecanismos da API/plataforma, com parâmetros de cada workload documentados e validados. |

O `AWS_DEPLOY_ENABLED=false` do Execução permanece enquanto o laboratório está encerrado. Esse bloqueio temporário não será usado como substituto do fluxo oficial; não se habilita AWS nem se altera o controle operacional como efeito colateral da padronização.

## Divergências encontradas na revisão

| Local | Evidência atual | Ajuste planejado |
| --- | --- | --- |
| Execução, `aws-deploy.yml` | Job único `deploy`; API separa resolução, plano, publicação da imagem validada, apply/destroy e rollout. | Reproduzir a estrutura da API adaptando somente recursos/dependências do serviço. Divergência veio da base da F4-03; o PR #2 não altera workflows. |
| Execução, imagem | CI publica artefato de imagem, mas CD executa novo `docker build`. | Consumir o artefato validado e conferir a árvore de código como a API. |
| Execução, contratos e bootstrap (corrigido no PR #3) | Nome do ECR vem de variable e há bootstrap específico por namespace; recuperação da referência não é executada integralmente. | Consumir outputs do owner Kubernetes via SSM e adaptar o mecanismo documentado de state/recuperação, com testes. Não copiar verificações de recursos da API sem adequá-las ao Execução. |
| RDS, plano | Job `plan` e job de aplicação geram planos em separado; a variante apply não transmite o mesmo arquivo entre eles. | Convergir para artefato do plano salvo, conforme padrão maduro. A dívida corresponde a `PIPE-002` e entra na padronização transversal solicitada, por PR próprio. Itens históricos como `PIPE-001` precisam de nova verificação antes de gerar trabalho. |
| Documentação de CI da API | Guia ainda cita três arquivos CI por ambiente, enquanto os workflows atuais usam `ci.yml`. Há trechos antigos de ownership também no guia. | Reconciliar README/guias com o modelo vigente, preservando o fluxo por estágio; não recriar arquivos legados por interpretação isolada do guia. |
| Execução, organização da Application (corrigido no PR #2) | `ProcessadorPreparacao` concentra roteamento, preparação, cancelamento e validação; chaves físicas e controle genérico de documentos estão na Application. | Separar os casos de uso com Request/Response/Validator/UseCase conforme o padrão existente; manter o adaptador de mensagens como entrada e encapsular chaves/JSON/revisões técnicas de banco na Infrastructure. Preservar o contrato JSON aprovado. |
| Execução, domínio e hospedagem (corrigido no PR #2) | `ItemEstoque` foi extraído, mas o agregado `Estoque` e a organização de casos de uso do contexto precisam ser reconciliados; o worker local constrói componentes de persistência dentro da API. | Reutilizar o agregado e as regras existentes quando aplicáveis, justificar os novos componentes de preparação exigidos pela RFC e organizar hospedagem/DI e adaptadores sem lógica de persistência na API. Não copiar o agregado de OS inteiro para Execução. |
| Execução, testes de Application (corrigido no PR #2) | Os fluxos estão cobertos por integração com DynamoDB, mas não há projeto de testes unitários de Application equivalente à referência. | Acrescentar testes unitários dos casos de uso e alinhar convenções xUnit/FluentAssertions, nomes BDD e organização existente, mantendo testes de integração e o gate. |

A padronização precisa ser verificável por matriz de jobs, triggers, permissões, environments, checks, artefatos, state, publicação, promoção e teardown. Nomes de checks diferem entre categorias já existentes (por exemplo, Quality gate 07 na API e 03 em Terraform); preservar a correspondência exata com cada ruleset. Essas categorias não autorizam estratégias diferentes de deploy/state/promoção.

## Sequência de entregas

| Ordem | Backlog e responsável | Entrega e critério de conclusão | Necessita AWS? |
| --- | --- | --- | --- |
| 1 | Revisão da F4-03/F4-04; Gabriel e alinhamento com Geovanna | Registrar matriz e corrigir divergências das esteiras por PRs próprios, começando pelo Execução. Convergir o legado identificado nos demais repositórios, sem alterar branches de trabalho da outra frente. Revisar nomenclatura/contratos. | Não para preparar e validar; deploy continua bloqueado. |
| 2 | F4-05, Gabriel — PR #2 | Ajustar a organização à estrutura do ZIP antes de solicitar aprovação: casos de uso/validadores, responsabilidades por camada e testes unitários de Application. Preservar a preparação/cancelamento persistentes e revalidar seus comportamentos. Revisão funcional separada da conclusão do CI/CD. | Não. |
| 3 | F4-04, Gabriel; Kubernetes como owner do ECR | Terraform de DynamoDB sob demanda, fila FIFO, DLQ, contratos SSM e identidade de workload; registries no repositório Kubernetes. Preparar offline e comprovar criação/uso/limpeza via esteiras na primeira janela autorizada. | Sim, somente para prova real. Avisar antes. |
| 4 | Restante F4-05, Gabriel | Extrair regras/testes de diagnóstico, `Servico`, início/finalização e baixa de estoque; implementar `AutorizarExecucao`, `ExecucaoIniciada` e `ExecucaoFinalizada`. Conectar SQS, confirmar após commit e tratar repetição/DLQ. Testes locais primeiro. | Só na validação SQS/workload real. |
| 5 | F4-06/07/08/09, Geovanna, coordenação de contratos | OS/Saga/outbox; Orçamento/Pagamento com SQL e fila próprios; Mercado Pago de teste; autenticação privada e Gateway. Gabriel adapta consumidores apenas ao contrato acordado. | Conforme provas de banco/rede e integração. |
| 6 | F4-10, Gabriel, revisão de Geovanna | Integrar os três serviços; BDD sucesso e compensação; duplicatas, resultado tardio, reinício e falhas; saldo e estados consistentes sem consultas cruzadas a bancos. | Integração real exige janela; boa parte dos testes é local. |
| 7 | F4-11, Gabriel no Execução; Geovanna nos demais | Datadog, propagação de correlação/trace, métricas/painéis de Saga e DLQ, logs seguros e recuperação observável. | Sim para evidência de ingestão real. |
| 8 | F4-12 a F4-14, ambos; documentação conduzida por Geovanna | Deploy completo pelas esteiras; testes/cobertura/Sonar; README/OpenAPI/Postman/C4; PDF, links e vídeo de até 15 minutos; entrega até 17/11 às 23h59. | Sim para evidência final; preparo documental é local. |

As frentes de Geovanna avançam em paralelo às de Gabriel conforme as dependências originais; não precisam esperar terminar toda a extração do Execução. O cronograma aprovado reserva 10–11/11 para serviços/infra mínima e riscos P0, 12–13 para integração, 14–15 para validação/observabilidade, 16 para documentação/gravação e 17 para conferência/envio. São marcos do backlog, não evidência de tarefas concluídas nem garantia de prazo.

## Como extrair o código existente

1. Mapear regra, dependências, testes, dados e consumidores antes de mover. Preservar a linguagem do domínio e os nomes existentes quando mantiverem a responsabilidade.
2. Reutilizar as regras e levar os testes para o serviço dono. O PR #2 extrai `ItemEstoque`, `Estoque`, mensagens/exceção relacionadas, os 19 testes da entidade e os 21 testes do agregado. Coordenação distribuída, persistência DynamoDB e outbox são código novo necessário à arquitetura aprovada; não existiam como transação entre serviços no monólito.
3. Na próxima parte, usar como fontes `Servico`, `IniciarDiagnosticoOrdemServicoUseCase`, `ReservarPecaInsumoUseCase`, `IniciarExecucaoServicoUseCase`, `FinalizarServicoUseCase` e as regras de baixa/finalização/cancelamento. Separar a regra física de Execução da atualização de OS e sua Saga; não mover o agregado de OS inteiro.
4. Adaptar infraestrutura EF/SQL para DynamoDB com condições/transações. A preservação das regras não implica copiar a transação de OS/estoque entre bancos nem compartilhar projetos Domain/migrations.
5. Retirar/encaminhar implementações antigas na API somente no corte integrado com a frente F4-06, Gateway e consumidores atualizados. Até lá, a cópia extraída não comprova migração do fluxo público.

O padrão por funcionalidade é Request DTO, Response DTO, Validator e UseCase/Handler com responsabilidade única. Portas de repositório seguem a modelagem do domínio; adaptadores e armazenamento ficam na Infrastructure. Novos componentes necessários à preparação/Saga devem ser justificados pelo contrato aprovado, sem substituir os nomes do domínio por estruturas genéricas. A correção do PR #2 separa `PrepararExecucaoUseCase` e `CancelarPreparacaoUseCase`, nomes derivados dos comandos aprovados e do sufixo técnico existente; sua organização está no diff, sem renomear as mensagens.

## Regras de negócio e pontos de integração a preservar

- Cliente/veículo existem e o mecânico é atribuído antes da OS; cadastros/catálogo continuam com OS e seus dados não são consultados diretamente pelo Execução.
- OS inicia Recebida; o mecânico inicia diagnóstico, define serviços e reserva peças. Reserva transfere saldo disponível para reservado, mantendo o total físico; baixa definitiva ocorre depois da finalização.
- A linguagem ubíqua exige diagnóstico completo antes do orçamento. O código atual também exige ao menos um serviço para `AguardarAprovacao`. O snapshot em `ExecucaoPreparada` do PR #2 ainda não comprova o fluxo humano completo de diagnóstico; o critério de prontidão deve ser preservado na extração/integração, sem transformar a cópia de campos em diagnóstico concluído.
- Sem aprovação da versão atual não há execução. Serviços individuais são iniciados/finalizados pelo mecânico; a OS só pode ser finalizada após todos os serviços concluídos.
- Recusa/insuficiência seguem os fluxos de cancelamento aprovados. Falha técnica transitória provoca recuperação, sem ser apresentada como decisão de negócio. Baixa e estorno são consequências dos fluxos da OS, não novas ações públicas arbitrárias de estoque.
- Pagamento deve ser confirmado pelo provedor antes da entrega. O estoque e os estados de integração/pagamento devem permanecer consistentes por filial e OS.

## Fluxo funcional final

OS abre e persiste Saga/outbox → `PrepararExecucao` → Execução diagnostica/reserva → `ExecucaoPreparada` ou `PreparacaoRecusada` → OS solicita `GerarOrcamento` → cliente decide a versão atual → aprovação produz `AutorizarExecucao` → `ExecucaoIniciada`/`ExecucaoFinalizada` → OS solicita `CriarCobranca` → pagamento verificado no Mercado Pago de teste → `PagamentoConfirmado` → OS permite entrega.

Na recusa/falha permanente anterior à execução física, OS solicita `CancelarPreparacao` e `CancelarOrcamento` e só registra compensação após confirmações. Reparo iniciado não sofre estorno fictício de estoque/serviço. `StatusOrdemServico` e seus valores públicos permanecem; integração e pagamento usam registros separados. Finalizada não significa paga.

## Implantação, laboratório e encerramento

Preparar código, Terraform, contratos e verificações locais antes de solicitar laboratório. Quando a prova de DynamoDB/SQS/EKS, banco isolado ou rede exigir AWS, avisar Gabriel para criar o laboratório e atualizar os environments. Conferir conta/state atuais; credenciais de um laboratório encerrado não comprovam acesso ao próximo.

Ordem de dependências: VPC → Kubernetes/ECR e dependências de banco → recursos próprios dos serviços → workloads → integração de autenticação/Gateway conforme suas dependências. Gateway por último. Nunca criar VPC/EKS/RDS/ECR dentro dos workloads. Destruir em ordem inversa, com VPC por último e respeitando dados, mensagens/outbox e dependências documentadas. Reunir cenários na mesma janela de validação sempre que possível.

## Resumo de implantação do PR #2

| Pergunta | Resposta |
| --- | --- |
| O que entra no código? | Regras de estoque extraídas, preparação/cancelamento persistentes, versões/idempotência, transação DynamoDB, outbox e transporte local substituível. |
| Onde roda agora? | API e DynamoDB Local via Compose; worker por arquivos somente em Development. |
| O que o merge aciona? | CI e CD de desenvolvimento conforme as regras existentes. Com `AWS_DEPLOY_ENABLED=false`, o deploy remoto fica bloqueado e alteração implantável não é promovida automaticamente como se tivesse sido implantada. |
| Qual código existente muda? | Apenas o repositório Execução no PR #2; o fluxo público do monólito permanece até o corte integrado. |
| O que será criado na AWS por este PR? | Nenhum recurso enquanto o bloqueio estiver ativo. A infraestrutura de DynamoDB/SQS e o consumidor remoto ainda são etapas seguintes. |
| O que foi validado? | 147 testes locais, nenhum ignorado; cobertura local consolidada de 96,62%; build/formato aprovados. Compose repetido com dois reinícios, reentrega e saldo 10→7/3→10/0. CI deve confirmar os gates sobre o commit final. |
| Quais limites importam? | Até 46 itens por lote atômico; outbox serial global neste incremento; arquivos não comprovam SQS; início/finalização física e integração completa ainda pendentes. |
| A esteira está integralmente padronizada? | Ainda não: divergências da base inicial estão identificadas acima e devem ser corrigidas antes de habilitar deploy. O PR #2 não altera workflows. |
| A organização já atende integralmente ao contexto do ZIP? | As correções identificadas foram implementadas: Request/Response/Validator/UseCase por funcionalidade, agregado Estoque extraído, portas tipadas, persistência/outbox/worker em Infrastructure e testes unitários próprios de Application. As funcionalidades restantes da F4-05 e a padronização do deploy continuam pendentes. |

## Aceite completo da Fase 4

Conferência direta do enunciado do ZIP: pelo menos três microsserviços com repositórios, infraestrutura e bancos próprios; SQL e NoSQL; REST quando necessário e mensageria; nenhum acesso a banco alheio; Saga e compensação demonstráveis; Mercado Pago real em ambiente de teste; testes unitários por serviço e fluxo BDD completo; cobertura mínima de 80% por serviço, preservando gates existentes mais fortes; SonarQube ou similar no CI; build/testes/qualidade/deploy Kubernetes independentes; branches protegidas; observabilidade herdada da Fase 3.

Por serviço: fonte, Dockerfile, manifests, pipelines, arquitetura documentada, cobertura com evidência no README e Swagger ou Postman atualizado. Na entrega: vídeo público/não listado de até 15 minutos mostrando fluxo completo, Saga/falhas, deploy automatizado e rastreamento; PDF com participantes/identificação, links dos repositórios e vídeo, diagrama final, escolha da Saga e justificativas técnicas. Os riscos P0 do backlog são permissões DynamoDB/SQS/laboratório, credenciais Mercado Pago, SQL com credenciais isoladas e acesso à análise de qualidade no CI. Não marcar esses itens concluídos com testes locais ou somente com cobertura.

## Resumo obrigatório nos próximos PRs

Todo envio deve informar: tarefa do backlog e comportamento entregue; código extraído e origem; componentes/repositórios e contratos afetados; recursos que serão criados/alterados e owner; quais esteiras serão acionadas; dependências de configuração/laboratório; testes e evidências reais; limitações, recuperação e próximos passos. Indicar expressamente se há deploy real ou apenas integração de código. Enviar o link para Gabriel realizar revisão e merge.

## Atualização — contrato ECR e padronização do Execução

O PR #58 de Kubernetes foi integrado em develop: define o registry `oficina-mecanica-execucao` e os contratos `kubernetes/execucao_ecr_repository_name` e `kubernetes/execucao_ecr_repository_url`. A existência dos recursos na AWS ainda depende de apply autorizado.

A correção da esteira do Execução está em PR próprio: cinco jobs equivalentes à API, consumo SSM, publicação do artefato de imagem validado, transferência do mesmo plano Terraform, state por conta/ambiente e recuperação pelos guardrails existentes. Remove o bootstrap alternativo e a dependência de `ECR_REPOSITORY_NAME`. A identificação do cluster/conta é registrada como data sources; nenhum EKS/ECR compartilhado é criado pelo workload. README detalha os pré-requisitos, retenção de artefatos, recuperação e limitações.

Validações locais: nove testes Node, Terraform 1.15.8 e actionlint aprovados. CI e prova AWS são evidências distintas; a prova AWS permanece pendente, com deploy desabilitado. As correções RDS/guia API e os demais itens do backlog continuam pendentes; esta mudança não declara uniformidade já atingida em todos os repositórios.

## Atualização — dados e mensageria da F4-04

PR #3 da esteira integrado em develop. O próximo incremento prepara tabela DynamoDB, entrada FIFO/DLQ próprias, contratos SSM e verificações de criação/limpeza, preservando os cinco jobs e gates. Os nomes físicos e sufixos SSM propostos estão no README para revisão antes de provisionar.

Há quatro testes Terraform com providers simulados e 15 testes Node locais aprovados. Novos guardrails bloqueiam destroy com mensagens/outbox pendentes, mas exigem produtores parados e preservação dos dados; não substituem backup nem tornam contagens aproximadas uma garantia transacional.

A identidade IAM do workload, conexão do runtime, prova real no laboratório e demais itens da F4-04 continuam pendentes. Não criar role ou assumir permissões de nodes silenciosamente. AWS permanece desabilitada. A janela será avisada previamente a Gabriel.

## Atualização — adaptadores SQS sem ativação

PR #4 da infraestrutura integrado em develop. O próximo incremento prepara `ConsumidorSqs` e `TransporteSqs`, reutilizando casos de uso, transação, deduplicação e publicador de outbox existentes. A nova porta de entrada `IProcessadorMensagem` é implementada pelo mesmo processador; a Application não recebe dependência AWS.

Há testes de contrato com cliente SDK simulado, relógio controlado e integração com DynamoDB Local, sem interpretar mocks como prova SQS real. A composição do runtime, a identidade do workload, a URL de resultados publicada pelo owner OS e a observabilidade distribuída continuam pendentes. Não alterar silenciosamente IAM nem direcionar resultados à entrada do Execução. Esta etapa mantém o ambiente remoto desligado e não conclui a F4-04/F4-05 inteira.

### Padronização obrigatória de estrutura — 10/11/2026

Por orientação explícita de Gabriel, as pastas de scripts foram removidas da API e do Execução. Lógica operacional e testes são mantidos nos workflows existentes; arquivos temporários são gerados fora do conteúdo versionado. O gate de Git Flow impede pastas de scripts e novos diretórios de topo em todo PR. Não acrescentar estruturas auxiliares sem alinhamento. Esta reorganização não altera recursos de infraestrutura, contratos de mensagens ou regras de negócio; AWS permanece desabilitada.

## Atualização — composição remota sem ativação

Após os PRs de estrutura e fluxo de trabalho, a preparação da F4-04 conecta os componentes existentes em `AddInfrastructureRemota`, com clientes DynamoDB/SQS fornecidos explicitamente pelo host. `ProcessamentoRemotoWorker` mantém consumo e publicação independentes, escopos por tentativa e encerramento cooperativo. O modo remoto não cria tabela nem clientes AWS; não escolhe credencial/role e impede registro simultâneo com o modo local. O README detalha os contratos aprovados de Execução e OS usados como origem das opções.

O host atual permanece no modo anterior: nenhuma variável ativa o remoto, e `Program.cs`, Terraform, manifests e esteiras não mudam neste incremento. A definição e prova da identidade IAM continuam necessárias antes de integrar a ativação no workload. O contrato de resultados da OS já foi aprovado na API #328, mas sua publicação real ainda exige apply autorizado. Não usar credenciais do runner ou role dos nodes por suposição.

Build Release e 180 testes locais passaram sem avisos, erros ou testes ignorados; DynamoDB Local e SQS simulado verificam retomada de outbox durante long polling, falhas de recepção/envio, ACK após persistência e encerramento. Esses testes não comprovam IAM/FIFO/DLQ reais. AWS permanece bloqueada; avisar Gabriel antes de precisar do laboratório. A F4-04/F4-05 ainda inclui ativação/prova remota e as regras de autorização/início/finalização física seguintes.

## Atualização — extração independente de Servico

PR #12 integrado. O incremento seguinte extrai a entidade `Servico`, enum `StatusServico`, quatro mensagens e dez cenários de teste da API, preservando nomes, transições e invariantes. Não altera OS, mensagens ou banco; não conecta ainda a entidade ao runtime/persistência e não conclui a F4-05. A origem e os limites estão em `extracao-preparacao.md`.

A implementação de handlers depende de registrar a passagem entre `AutorizarExecucao`, ações físicas do mecânico e eventos/compensação. Gabriel confirmou a preservação do início manual do modelo DDD; o esclarecimento está em PR próprio da RFC. Este incremento extrai apenas a entidade e seus testes, sem antecipar handlers ou o corte público. A validação local passou em 190 testes, zero ignorados, cobertura consolidada de 96,70%, build sem avisos e formato verificado. AWS permanece desligada.

## Atualização — autorização e ciclo físico persistidos

API #335 e Execução #13 integrados em develop. Gabriel confirmou preservar `servicoId` e incluir `servicoCatalogoId` no snapshot; o ajuste da RFC segue em PR próprio antes do corte coordenado com o produtor OS. O Execução implementa autorização sem evento fictício, casos de uso internos de início/finalização manual e finalização explícita com baixa atômica. Reutiliza entidade e estoque extraídos, unidade de trabalho DynamoDB, deduplicação e outbox.

Cancelamento pode confirmar antes do início físico; após ele, não libera reserva ou anuncia compensação. A disputa compara o mesmo documento/revisão. Os testes locais usam DynamoDB Local, concorrência forçada, falha de commit e verificação de saldo/estado/outbox; não comprovam SQS/IAM reais. Operações manuais ainda não possuem rotas/políticas HTTP, e o corte público fica para integração com OS/Gateway. Não declarar F4-05/F4-10 concluídas sem essas etapas.

Nenhum workflow, recurso Terraform, contrato SSM ou ativação de workload remoto muda. AWS permanece bloqueada, sem consultas/apply/destroy. Merges exclusivamente por Gabriel; avisar antes da janela de laboratório.
