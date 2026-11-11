---
name: oficina-mecanica
description: Aplica o fluxo de implementação, revisão e preparação de PRs do Oficina Mecânica ao serviço Execução, consultando a skill canônica da API. Use em tarefas deste repositório e em suas integrações com os demais serviços.
---

# Encaminhamento para o fluxo canônico

Antes de implementar ou revisar um PR, ler o `AGENTS.md` deste repositório e o procedimento completo em `../oficina-mecanica-api/.agents/skills/oficina-mecanica/SKILL.md`, considerando o caminho a partir da raiz do Execução. Este arquivo não mantém uma segunda versão do procedimento.

Se o clone irmão não estiver disponível, consultar a [skill da API na branch develop](https://github.com/oficina-mecanica-soat/oficina-mecanica-api/blob/develop/.agents/skills/oficina-mecanica/SKILL.md), confirmando que a referência é a adequada à tarefa. Não continuar com uma cópia antiga presumida; informar e perguntar se a fonte necessária não puder ser lida.

Aplicar o procedimento canônico junto das regras locais. O README do Execução continua sendo a fonte do funcionamento deste componente; ADRs/RFCs e backlog da API mantêm as decisões compartilhadas. Não instalar outra skill, criar agente, pasta auxiliar ou alterar configuração local para fazer esse encaminhamento.

Uma sessão iniciada fora do Git não comprova carregamento automático: localizar a raiz e ler explicitamente as instruções. A aprovação de PR não autoriza AWS ou merge automático.
