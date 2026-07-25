# Implementation Plan: Persistência de Configurações e Estado de Execução Pós-Atualização Velopack

Este documento estabelece o diagnóstico da perda de configurações/estado durante o ciclo de atualização do Velopack e detalha o plano de implementação dividido em Sprints para garantir a preservação de dados e a retomada automática da aplicação.

> [!IMPORTANT]
> **User Review Required**
> Este plano aborda duas causas raiz:
> 1. Armazenamento de configurações no diretório de instalação do executável (`app-1.x.x`), que é substituído a cada atualização do Velopack.
> 2. Ausência de rastreamento do estado de execução do serviço (`_isRunning`) para reativação automática pós-reinicialização.
> Por favor, avalie a divisão em Sprints e a estratégia de migração para `%AppData%\ConnectML`.

---

## 🔍 Diagnóstico e Causa Raiz

1. **Reset de Configurações**:
   Atualmente, o `MainWindow.xaml.cs` faz I/O no arquivo relativo `appsettings.json`. Quando o Velopack executa uma atualização de versão (ex: `1.2.0` -> `1.2.1`), ele cria uma pasta de versão limpa (`%LocalAppData%\ConnectML\app-1.2.1`) contendo os arquivos padrão do pacote `publish`. As alterações feitas pelo usuário na pasta `app-1.2.0` não são copiadas para a pasta `app-1.2.1`.

2. **Perda do Estado do Serviço**:
   Quando a aplicação é atualizada via `ApplyUpdatesAndRestart()`, o aplicativo encerra e reinicia na nova versão. Como o estado de funcionamento em tempo de execução (`_isRunning`) não era armazenado persistentemente em disco, a aplicação iniciava em estado parado (*Offline*).

---

## 🎯 Estratégia Arquitetural Proposta

### 1. Migração para o Diretório Persistente do Usuário (`%AppData%\ConnectML`)
- O local de armazenamento principal do arquivo de configuração passará a ser:
  `%AppData%\ConnectML\appsettings.json` (ou `Environment.SpecialFolder.ApplicationData` / `ConnectML\user_config.json`).
- **Migração Transparente no Startup**:
  Se a aplicação iniciar e não encontrar a configuração no `%AppData%\ConnectML`, ela checará se existe um `appsettings.json` local (legado no diretório da aplicação), carregará os dados e migrará automaticamente para o `%AppData%\ConnectML`.

### 2. Persistência do Estado de Execução (`WasServiceRunning`)
- Incluir a propriedade `WasServiceRunning` (boolean) no modelo de configuração/estado.
- Sempre que o serviço for iniciado (`BtnStartStop_Click`) ou parado, ou imediatamente antes da chamada de `ApplyUpdatesAndRestart()`, o estado atual de execução será salvo em disco.
- Ao abrir a aplicação (`Window_Loaded`), se `WasServiceRunning == true` ou `AutoStartEnabled == true`, o ConnectML acionará automaticamente a inicialização do driver/serviço, garantindo operação contínua e sem intervenção manual pós-update.

---

## 🚀 Divisão de Ações em Sprints

### Sprint 4.1A: Arquitetura de Persistência em %AppData% e Migração
- **Objetivo**: Mover o local de gravação/leitura de configurações de `appsettings.json` (local/relativo) para `%AppData%\ConnectML\appsettings.json`.
- **Arquivos Afetados**:
  - `[MODIFY]` [MainWindow.xaml.cs](file:///c:/Antigravity/ConnectML/ConnectML.UI/MainWindow.xaml.cs): Atualizar obtenção de caminho dinâmico via `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)`.
  - `[MODIFY]` Adicionar lógica de fallback e migração de arquivos no `LoadSettings()`.

### Sprint 4.1B: Rastreamento e Auto-Resumo do Estado do Serviço
- **Objetivo**: Persistir o status do loop de comunicação (`_isRunning`) e promover a auto-inicialização pós-restart do Velopack.
- **Arquivos Afetados**:
  - `[MODIFY]` [AppConfig.cs](file:///c:/Antigravity/ConnectML/ConnectML.UI/Models/AppConfig.cs) (ou classe de modelo referente): Adicionar campo `WasServiceRunning`.
  - `[MODIFY]` [MainWindow.xaml.cs](file:///c:/Antigravity/ConnectML/ConnectML.UI/MainWindow.xaml.cs):
    - Atualizar `SaveSettings()` e `BtnStartStop_Click()` para gravar `WasServiceRunning = _isRunning`.
    - Garantir flush do estado antes de chamar `_updateManager.ApplyUpdatesAndRestart()`.
    - Atualizar `Window_Loaded` para verificar se `WasServiceRunning` é verdadeiro e disparar a execução imediata.

### Sprint 4.1C: Validação Prática de Atualização (Release v1.2.2)
- **Objetivo**: Empacotar e publicar a versão `v1.2.2` no GitHub Releases via `vpk` para testar a migração em tempo real.
- **Roteiro de Testes**:
  1. Executar versão `v1.2.1` configurada com IP Siemens/Webhook customizado e serviço **Ativo**.
  2. Disparar a atualização para `v1.2.2` via aviso na UI.
  3. Verificar se o app reabre em `v1.2.2` com todos os campos preenchidos e o serviço **automaticamente rodando**.

---

## ❓ Open Questions para Discussão

> [!NOTE]
> 1. Prefere que as configurações do usuário fiquem no diretório `%AppData%\ConnectML` (Roaming, segue o usuário na rede) ou `%LocalAppData%\ConnectML` (Local da máquina)?
> 2. Se a aplicação for fechada manualmente pelo usuário (clicando no 'X' para sair), o estado de "estava rodando" deve ser preservado para a próxima inicialização, ou apenas em atualizações do Velopack?
