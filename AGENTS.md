# Instruções do repositório

- Antes de alterar, examine o estado Git e o código relacionado. As decisões canônicas estão no repositório irmão `oficina-mecanica-api`, em `docs/architecture/adrs`, `docs/architecture/rfcs` e `docs/projeto/backlog-tecnico.md`; consulte também os links do README. Preserve alterações existentes.
- Para trabalho transversal ao projeto, leia `.agents/skills/oficina-mecanica/SKILL.md` antes de implementar. Ela complementa estas instruções e não substitui os documentos de arquitetura.
- Preserve DDD, linguagem ubíqua, Clean Architecture, use cases e contratos existentes. Antes de extrair um serviço, identifique regras, testes, dados e consumidores; não presuma que a migração já ocorreu.
- Use português para conceitos do domínio e inglês para convenções técnicas consolidadas. Identificadores C# seguem o padrão existente sem acentos; mensagens, logs, documentação e PRs devem usar português brasileiro correto, com acentos e UTF-8.
- Aplique SOLID, Clean Code, KISS e DRY com julgamento: responsabilidades claras, testes úteis e sem abstrações antecipadas. Não renomeie contratos existentes sem avaliar consumidores.
- Para CI/CD, compare a alteração com os workflows maduros da API, VPC e Kubernetes; preserve Git Flow, quality gates, promoções, environments e guardrails. Não enfraqueça proteções para facilitar entrega.
- Execute validações proporcionais e informe resultados reais. Não exponha credenciais, secrets ou dados sensíveis. Operações reais de infraestrutura e alterações destrutivas exigem autorização específica.
- Para alterações .NET, consulte o README e valide com `dotnet restore OficinaMecanica.Execucao.sln`, `dotnet build OficinaMecanica.Execucao.sln --no-restore` e testes pertinentes; use `dotnet format OficinaMecanica.Execucao.sln --verify-no-changes --no-restore` quando aplicável. Para Terraform, consulte os workflows e valide formatação e configuração sem executar deploy.

## Estrutura obrigatória em todos os PRs

- Não versionar pastas `scripts/`, `.github/scripts` ou equivalentes em outros níveis. Não mover os mesmos arquivos para outra pasta para contornar essa regra.
- Manter a estrutura existente; não acrescentar diretórios de topo ou novas estruturas auxiliares sem alinhamento explícito. Novas funcionalidades devem seguir as camadas e convenções já aprovadas dentro de `src`, `tests`, `infra` e `docs`.
- Validações operacionais e seus testes ficam nos próprios workflows; arquivos temporários gerados durante a execução ficam somente no diretório temporário do runner, fora do conteúdo versionado.
- A existência de pastas de scripts em outro repositório não autoriza copiá-las. Preservar as proteções e os testes ao ajustar a organização.
- Conferir o gate de estrutura antes de enviar qualquer PR, inclusive documental. Não enfraquecer esse gate para publicar pastas não alinhadas.

## Aplicação do fluxo de trabalho

- Antes de editar, identificar a raiz do repositório, ler estas regras e o procedimento canônico da skill. Se a sessão começou em um diretório externo, não presumir que uma mudança de diretório em ferramenta carregou as instruções automaticamente.
- O procedimento canônico fica em `oficina-mecanica-api/.agents/skills/oficina-mecanica/SKILL.md`; no Execução, a skill local apenas encaminha para essa fonte. Não manter cópias divergentes do procedimento nem instalar configuração local sem tarefa explícita para isso.
- Antes de publicar qualquer PR, revisar o diff e os caminhos adicionados, executar o gate de estrutura do CI e informar validações realmente observadas. Preservar o gate; não adaptar o bloqueio para permitir uma estrutura não alinhada.
- Com ajustes autorizados prontos, abrir o PR e informar comportamento, reaproveitamento, contratos, implantação prevista, testes e pendências. Gabriel realiza o merge. Aprovação de código não autoriza apply/deploy/destroy: manter AWS bloqueada até janela explicitamente autorizada.
