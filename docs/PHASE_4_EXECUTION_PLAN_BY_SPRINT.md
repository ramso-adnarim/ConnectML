# PLANO DE EXECUÇÃO POR SPRINT - FASE 4

Este documento detalha o planejamento tático para a implementação da Fase 4 (Enterprise & UX) do ConnectML. O trabalho será dividido em Sprints focadas e seguras, minimizando riscos e entregando valor incrementalmente.

## Sprint 1: Fundação, Versionamento e Single Instance
**Objetivo:** Preparar o ambiente, atualizar a versão global e garantir a execução de instância única.
- **Tarefas:**
  1. **Criar a nova branch de trabalho:** `git checkout -b feature/fase4-enterprise` a partir da `main`.
  2. Atualizar globalmente a versão para **V1.1.0** (arquivos `.csproj`, configuração do Velopack, Títulos da UI e cabeçalhos de logs).
  3. Implementar a lógica de `Mutex` no `Program.cs` para bloquear múltiplas instâncias.
  4. Adicionar lógica de interprocessamento ou chamadas Win32 nativas para trazer a instância existente para o primeiro plano caso uma nova tentativa de abertura ocorra.

## Sprint 2: UI Dinâmica de Configuração e Validação Webhook
**Objetivo:** Refatorar a seção de "Configuração" para suportar a adição dinâmica de campos.
- **Tarefas:**
  1. Substituir a seleção atual por uma lista de adição dinâmica (ex: `ItemsControl` em uma tabela).
  2. Implementar regras e comportamentos visuais de limite: Mínimo de 1 (botão de remover "-" some/desabilita) e Máximo de 3 (botão de adicionar "+" some/desabilita).
  3. Disponibilizar as opções de ComboBox: `Verdadeiro/Falso` (padrão), `Contador` e `Nome da Peça (Part Number)`.
  4. Adicionar validação Webhook REST no clique do botão "Iniciar": validar se o template do payload inserido contém tags (ex: `{{ PartNumber }}`) que representem as seleções ativas. Exibir aviso in-line bloqueante em caso de erro, evitando o uso de `MessageBox`.

## Sprint 3: Integração S7 Avançada e UI Responsiva
**Objetivo:** Adaptar o painel do CLP S7 e implementar a complexa escrita de Strings.
- **Tarefas:**
  1. Tornar o painel de integração Siemens S7 responsivo: a visibilidade das caixas de texto `DB (Booleana)`, `DB (Inteira)` e `DB (PartNumber)` dependerá dos campos adicionados na configuração geral (Sprint 2).
  2. Manter a `DB (Status)` sempre visível independentemente.
  3. Adicionar o botão de Teste transiente ao lado da `DB (PartNumber)` que faça um *toggle* de escrita de strings fictícias ("True" e "False") no CLP para validação.
  4. Implementar a rotina de envio de String lida do QIF (`productname`) utilizando a classe adequada do `S7NetPlus` para garantir a formatação correta do cabeçalho de 2 bytes (Max Length, Actual Length) no CLP.

## Sprint 4: Motor de Retry Progressivo (Auto-Start)
**Objetivo:** Garantir a resiliência da aplicação quando iniciada em ambientes desfavoráveis.
- **Tarefas:**
  1. Desenvolver uma janela de alerta não-bloqueante customizada, com *countdown* visual de 6 segundos, a ser exibida caso a conexão inicial (no "Iniciar") falhe.
  2. Desenvolver o loop assíncrono de *retry* progressivo (a cada 5 segundos) após o fechamento da janela de alerta.
  3. Garantir que, durante o loop, a interface seja trazida ao primeiro plano (saia do modo minimizado), e que exceções sejam enviadas ao Serilog (sem novos pop-ups).
  4. Implementar o *Escape Hatch*: cancelar imediatamente a rotina de *retry* caso o usuário clique em "Parar".
  5. Ajustar o fluxo de sucesso: ao reconectar, o app deve parar o loop, iniciar o monitoramento normalmente e retornar automaticamente ao estado minimizado no System Tray.

## Sprint 5: Interações Avançadas de UX (System Tray & Menu)
**Objetivo:** Polimento da interface e notificações visuais de sistema para o *customer*.
- **Tarefas:**
  1. Integrar os balões de notificação do Windows (`ShowBalloonTip`) a partir do ícone do Tray, alertando de forma assíncrona sobre arquivos processados (sucessos com PartNumber) e falhas críticas.
  2. Implementar o botão *Hamburger Menu* (ícone de 3 linhas horizontais) no topo da interface principal.
  3. Adicionar comportamento responsivo: ao clicar no menu, o painel esquerdo inteiro (Configurações/Integração) será colapsado e a área de Logs preencherá 100% da largura da janela.
  4. Homologação final da branch `feature/fase4-enterprise` e preparação para merge.
