# Instruções do repositório

- Antes de alterar, examine o estado Git e o código relacionado. As decisões canônicas estão no repositório irmão `oficina-mecanica-api`, em `docs/architecture/adrs`, `docs/architecture/rfcs` e `docs/projeto/backlog-tecnico.md`; consulte também os links do README. Preserve alterações existentes.
- Para trabalho transversal ao projeto, leia `.agents/skills/oficina-mecanica/SKILL.md` antes de implementar. Ela complementa estas instruções e não substitui os documentos de arquitetura.
- Preserve DDD, linguagem ubíqua, Clean Architecture, use cases e contratos existentes. Antes de extrair um serviço, identifique regras, testes, dados e consumidores; não presuma que a migração já ocorreu.
- Use português para conceitos do domínio e inglês para convenções técnicas consolidadas. Identificadores C# seguem o padrão existente sem acentos; mensagens, logs, documentação e PRs devem usar português brasileiro correto, com acentos e UTF-8.
- Aplique SOLID, Clean Code, KISS e DRY com julgamento: responsabilidades claras, testes úteis e sem abstrações antecipadas. Não renomeie contratos existentes sem avaliar consumidores.
- Para CI/CD, compare a alteração com os workflows maduros da API, VPC e Kubernetes; preserve Git Flow, quality gates, promoções, environments e guardrails. Não enfraqueça proteções para facilitar entrega.
- Execute validações proporcionais e informe resultados reais. Não exponha credenciais, secrets ou dados sensíveis. Operações reais de infraestrutura e alterações destrutivas exigem autorização específica.
- Para alterações .NET, consulte o README e valide com `dotnet restore OficinaMecanica.Execucao.sln`, `dotnet build OficinaMecanica.Execucao.sln --no-restore` e testes pertinentes; use `dotnet format OficinaMecanica.Execucao.sln --verify-no-changes --no-restore` quando aplicável. Para Terraform, consulte os workflows e valide formatação e configuração sem executar deploy.
