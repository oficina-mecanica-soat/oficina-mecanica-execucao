---
name: oficina-mecanica
description: Orienta alterações no projeto Oficina Mecânica que envolvam arquitetura, contratos, aplicação ou infraestrutura entre repositórios.
---

# Oficina Mecânica

Use as instruções do repositório ativo e consulte os documentos canônicos da API em `docs/architecture/adrs`, `docs/architecture/rfcs` e `docs/projeto/backlog-tecnico.md`. Em outro clone, localize o repositório `oficina-mecanica-api` ao lado; se indisponível, informe a limitação e não invente contratos.

Antes de implementar, identifique o comportamento existente, produtores e consumidores, regras de domínio, testes e referência equivalente. Respeite decisões aceitas; proponha ADR sucessora para mudança arquitetural e RFC quando contratos ou coordenação exigirem detalhamento. Registre dúvidas em vez de presumir decisões não aprovadas.

Na evolução para serviços independentes, cada serviço é dono dos próprios dados; não acesse diretamente o banco de outro serviço. DynamoDB e SQS são escolhas acordadas; SNS somente quando houver necessidade. Defina contratos, idempotência, falhas e compensações antes de implementar a Saga. Confirme permissões do laboratório antes de depender de provisionamento real.

Siga o padrão real de CI/CD da API, VPC e Kubernetes, comparando workflows, gates, Git Flow, promoções, environments, permissões e guardrails. Preserve os contratos SSM e o papel do Datadog. Não dispare deploy, apply ou destroy sem autorização específica.

Use português brasileiro claro e acentuado em textos, mensagens, logs e PRs; conceitos do domínio em português e termos técnicos consolidados em inglês, seguindo identificadores existentes sem acentos. Aplique SOLID, Clean Code, KISS e DRY sem criar complexidade desnecessária. Verifique testes e evidências reais; preserve trabalho alheio e não exponha segredos.
