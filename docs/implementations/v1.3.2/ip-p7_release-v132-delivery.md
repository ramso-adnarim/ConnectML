# Relatório de Entrega: Sprint 7 - Release e Deployment v1.3.2

## 1. Visão Geral da Entrega
A **Versão 1.3.2** do ConnectML foi desenvolvida para resolver pontualmente três comportamentos reportados em ambiente de chão de fábrica:
1. **Ativação Universal do HUD**: Garantir que o HUD seja exibido sempre que a aplicação for enviada para a bandeja, inclusive durante a inicialização automática do Windows (Startup) enquanto a conexão assíncrona com o CLP ainda está sendo estabelecida.
2. **Controle Rigidamente Estrito do Reinício Automático**: O serviço agora só é iniciado automaticamente se a checkbox **"Reinício automático"** estiver explicitamente marcada pelo usuário e salva no arquivo de configuração, eliminando o auto-start indevido causado pela flag `WasServiceRunning`.
3. **Blindagem e Migração de Configurações no Velopack**: O algoritmo `EnsureSettingsMigrated()` foi completamente reescrito para proteger as configurações do usuário em `%AppData%\ConnectML\appsettings.json`, impedindo que os templates de fábrica de novas versões sobrescrevam dados existentes e recuperando configurações de clientes que atualizaram da v1.2.0.

---

## 2. Detalhamento dos Componentes Alterados

### 2.1. Ativação do HUD no Startup e Bandeja
- **Arquivo**: `ConnectML.UI/MainWindow.xaml.cs`
- **Modificações**:
  - Removida a trava `if (_isRunning)` de `BtnMinimize_Click`. Ao ocultar para a bandeja, `_overlayWindow.Show()` é invocado universalmente.
  - Em `StartService()`, ao concluir a conexão física com o CLP e definir `_isRunning = true`, verifica se a janela principal está oculta (`!IsVisible`) e sincroniza o HUD.

### 2.2. Controle Estrito de Reinício Automático
- **Arquivos**: `ConnectML.UI/MainWindow.xaml` e `ConnectML.UI/MainWindow.xaml.cs`
- **Modificações**:
  - Adicionado evento `Click="ChkAutoStart_Click"` na checkbox `ChkAutoStart` com chamada a `SaveSettings()`, gravando a preferência no disco imediatamente ao clique.
  - Em `Window_Loaded`, a inicialização automática foi restrita a `ChkAutoStart.IsChecked == true && (_lastRunSuccessful || _wasServiceRunning)`.

### 2.3. Persistência Resiliente contra Sobrescrita em Upgrades
- **Arquivo**: `ConnectML.UI/MainWindow.xaml.cs`
- **Modificações**:
  - `EnsureSettingsMigrated()`:
    - Se `%AppData%\ConnectML\appsettings.json` já existir e for customizado, ele **nunca** é sobrescrito.
    - Se `%AppData%\ConnectML\appsettings.json` não existir ou for apenas o template inicial, varre pastas `app-*` de versões estritamente anteriores e filtra templates via `IsDefaultTemplateConfig()`, priorizando configurações reais do cliente.

---

## 3. Unificação de Versionamento (v1.3.2)
- **`ConnectML.Core.csproj`**: `<Version>1.3.2</Version>`
- **`ConnectML.Infrastructure.csproj`**: `<Version>1.3.2</Version>`
- **`ConnectML.Simulator.csproj`**: `<Version>1.3.2</Version>`
- **`ConnectML.UI.csproj`**: `<Version>1.3.2</Version>`
- **`MainWindow.xaml`**: `Title="ConnectML - V1.3.2"` e `Versão 1.3.2`
- **`Program.cs`**: `FindWindow(null, "ConnectML - V1.3.2")`

---

## 4. Testes e Validação
- **Teste Funcional Manual**: Executado pelo usuário via `dotnet run`. Aprovada a inicialização com checkbox desmarcada (não inicia sozinho) e marcada (inicia, minimiza e HUD surge imediatamente).
- **Teste de Migração em Laboratório**: Validada a recuperação de arquivo customizado em `app-1.2.0` com prioridade sobre template recente de `app-1.3.2`.
- **Compilação**: `dotnet build ConnectML.sln -c Release` aprovada com 0 erros.
