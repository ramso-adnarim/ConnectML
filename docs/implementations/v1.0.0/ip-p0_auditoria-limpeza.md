# Implementation Plan: Sprint 0 - Auditoria, Limpeza e Desinfecção

Este documento segue as regras definidas em `METHODOLOGY_TRANSFER.md` e o planejamento de `CONNECTML_IMPLEMENTATION_ROADMAP.md` (Sprint 0).

> [!IMPORTANT]
> **User Review Required**
> As seguintes ações estruturais serão executadas na Sprint 0. Por favor, valide esta abordagem antes que eu comece a modificar os arquivos:
> 1. **Limpeza da pasta `docs/`**: Documentos históricos (ex: `PHASE_2_REVIEW.md`, `PHASE_4_ARCHITECTURAL_OVERVIEW.md`) serão movidos para `docs/implementations/v1.0.0/` e renomeados sob a nomenclatura `ip-pX_...md`.
> 2. **Criação de `AI_CODEBASE_MAP.md`**: Um novo documento será criado na raiz de `docs/` para estabelecer o glossário inicial e documentar as regras essenciais do projeto (como os conceitos de Inbound, Webhook Outbound, Protocolos, etc).
> 3. **Sem alterações de código**: Conforme regra, todo o mapeamento de UI (cores em hardcode nos XAML) e concorrência (Task.Run em Code-Behind) foi concluído e catalogado aqui, mas não será refatorado agora.

## Open Questions

1. Como deseja que os arquivos históricos sejam mapeados na renomeação? Ex: `PHASE_2_REVIEW.md` vira `ip-p2_review.md`?
2. Existem subpastas em `docs/` (`PHASE_2_DOCS`, `PHASE_3_DOCS`, `QIF`). Devo arquivá-las também na pasta `implementations/` ou deixá-las como estão por enquanto?

## Proposed Changes

### Governança da Documentação (A serem executadas nesta Sprint)

#### [NEW] `docs/implementations/v1.0.0/ip-p2_review.md`
(Migrado a partir de `docs/PHASE_2_REVIEW.md`)

#### [NEW] `docs/implementations/v1.0.0/ip-p4_architectural-overview.md`
(Migrado a partir de `docs/PHASE_4_ARCHITECTURAL_OVERVIEW.md`)

#### [NEW] `docs/implementations/v1.0.0/ip-p4_execution-plan.md`
(Migrado a partir de `docs/PHASE_4_EXECUTION_PLAN_BY_SPRINT.md`)

#### [NEW] `docs/implementations/v1.0.0/ip-context_project-context.md`
(Migrado a partir de `docs/PROJECT_CONTEXT.md`)

#### [DELETE] Arquivos legados soltos na raiz de `docs/`
Serão deletados após a cópia segura (ex: `PHASE_2_REVIEW.md`, etc.).

#### [NEW] `docs/AI_CODEBASE_MAP.md`
Arquivo inicializado com a definição macro do domínio (Conceitos base de Protocolos CLP, Monitoramento de Diretório).

---

### Auditoria Concluída (Resultados Mapeados)
*Nota: Nenhuma destas refatorações será feita nesta Sprint 0. Apenas mapeadas para as Sprints 1 e 3.*

**1. Auditoria da Camada de Visualização (XAML):**
Identificamos dezenas de cores "hardcoded" (`Background`, `Foreground`, `BorderBrush`) configuradas diretamente via `Hex` que precisarão migrar para o dicionário de semântica na **Sprint 3**:
- `ConnectML.UI/MainWindow.xaml`
- `ConnectML.UI/AlertCountdownWindow.xaml`
- `ConnectML.UI/SecurityUnlockWindow.xaml`

**2. Auditoria de Concorrência e Code-Behind:**
- Foram identificadas lógicas de loop e threads background orquestradas na `ConnectML.UI/MainWindow.xaml.cs` (ex: `Task.Run` para `RunProgressiveRetryLoopAsync`). Na **Sprint 1**, isso será extraído para Singleton Services e encapsulado com segurança.

## Verification Plan

### Manual Verification
- Assim que você aprovar este plano, eu prosseguirei com os comandos de mover/renomear os arquivos dentro da pasta `docs/`.
- Após a conclusão, solicitarei que você faça uma revisão visual da árvore de pastas e valide o conteúdo gerado em `docs/AI_CODEBASE_MAP.md`.
