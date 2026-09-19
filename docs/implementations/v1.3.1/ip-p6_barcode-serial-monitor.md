# Implementation Plan: Fase 6 - Mecanismo de Monitoramento e Transporte do Leitor de Código de Barras (Versão 1.3.1)

Este documento estabelece o projeto de arquitetura, análise de impacto e o roteiro técnico detalhado para a implementação da funcionalidade de **Monitoramento e Transporte Serial do Leitor de Código de Barras com Intercepção de Comandos e Simulação de Teclado no MeasurLink**, projetada para a versão **1.3.1** do ConnectML.

> [!IMPORTANT]
> **User Review Required**
> 1. **Isolamento e Concorrência**: O monitoramento das portas COM opera de maneira 100% assíncrona e desacoplada em segundo plano. Falhas de porta (desconexão física, conflito de abertura de porta serial ou buffers inválidos) são capturadas de forma resiliente e nunca interrompem nem degradam a esteira principal de processamento de arquivos QIF/XML nem as comunicações com PLCs e Webhooks.
> 2. **Separação por Portas com0com**: A arquitetura pressupõe que o leitor de código de barras físico ou emulado esteja associado a uma porta de entrada (ex: `COM1` ou `COM10`), e o software de metrologia MeasurLink esteja escutando uma porta de saída virtual (ex: `COM11`) pertencente a um par virtual gerenciado externamente pelo utilitário `com0com`.
> 3. **Intercepção vs. Despacho Serial**: Leituras normais são despachadas integralmente para a porta serial de saída virtual (MeasurLink). Quando a leitura coincidir com uma "palavra-chave" configurada, o repasse serial é suprimido e a aplicação aciona a rotina de comando em foco.
> 4. **Automação de Tela no MeasurLink (Win32 Focus + Simulação de Teclas)**:
>    - Ao disparar um comando interceptado, o ConnectML localiza a janela ativa do MeasurLink via Win32 API (`EnumWindows` / `GetWindowText` buscando correspondência com `"MeasurLink"`).
>    - Traz a janela do MeasurLink para o primeiro plano (`SetForegroundWindow`).
>    - Simula o pressionamento sequencial de teclas configurado (ex: `Alt + Q + O` para o comando inicial **Desfazer**).
> 5. **Extensibilidade e Customização via JSON (`appsettings.json`)**:
>    - A lista de comandos disponíveis (`AvailableCommands`) é persistida no arquivo JSON de configurações da aplicação.
>    - Usuários avançados e integradores podem editar o arquivo JSON para cadastrar novos atalhos de teclado e janelas-alvo sem necessidade de recompilação do software.
>    - A interface do ConnectML lê dinamicamente esses comandos ao preencher a combobox de seleção de ações.

---

## 🔍 1. Contexto Operacional e Diagnóstico de Necessidade

### 1.1 Cenário Atual
Na célula de manufatura e metrologia com **MeasurLink (Mitutoyo)**:
- O operador utiliza leitores de código de barras para apontar dados complementares de processo (ordens de produção, lote ou identificação de peça).
- Determinadas leituras especiais não representam dados dimensionais, mas sim ordens de controle na tela do software de metrologia (exemplo: desfazer a medição de uma característica incorreta, avançar lote ou cancelar leitura).
- No MeasurLink, a ação de **Desfazer** é acionada pelo atalho de teclado `Alt + Q + O`.

### 1.2 Solução Proposta
A versão **1.3.1** transforma o **ConnectML** em uma ponte serial inteligente com automação de tela:
1. **Monitoramento Serial Paralelo**: Escuta eventos da porta COM de entrada do leitor sem travar a interface gráfica.
2. **Avaliação por Regras de Intercepção**: Compara a string recebida contra a lista de palavras-chave cadastradas.
3. **Fluxo Condicional**:
   - **Caso Normal**: Transmite os dados sem alteração para a porta COM de saída (conectada ao MeasurLink via par virtual `com0com`).
   - **Caso Palavra-Chave**: Descarta o encaminhamento serial, busca a janela do MeasurLink no Windows, concede o foco de primeiro plano e simula a combinação de teclas associada ao comando.
4. **Comandos Configuráveis via JSON**: Comandos são definidos com nome, atalho e janela-alvo no `appsettings.json`, permitindo flexibilidade total para novos atalhos.
5. **Logs Objetivos**: Registra de forma sucinta no arquivo de log e na aba visual LOG o status da transmissão ou da execução do atalho simulado.

---

## 🎯 2. Estratégia Arquitetural e Fluxo de Dados

```mermaid
flowchart TD
    subgraph Hardware / Virtual I/O
        BCR[Leitor de Código de Barras\n(Porta COM Entrada)] -->|Leitura Serial / CRLF| BSM[BarcodeSerialMonitorService]
    end

    subgraph "ConnectML 1.3.1 (Core & Infrastructure)"
        BSM --> CHECK{A leitura coincide\ncom Palavra-Chave?}
        
        %% Caminho Normal
        CHECK -->|NÃO: Dado Normal| DISPATCH[Despacho Serial de Saída]
        DISPATCH -->|Escreve na COM de Saída| COM_OUT[Porta COM Virtual Saída\n(com0com)]
        DISPATCH --> LOG_OK[Log Objetivo: Sucesso Transporte]

        %% Caminho Interceptado
        CHECK -->|SIM: Palavra-Chave| INTERCEPT[Interceptador de Regra]
        INTERCEPT --> CMD_LOOKUP[Obtém Definição do Comando\n(Carregado do appsettings.json)]
        CMD_LOOKUP --> WIN_FIND[Localiza Janela MeasurLink\n(Win32 EnumWindows)]
        WIN_FIND --> WIN_FOCUS[Coloca Janela em Foco\n(SetForegroundWindow)]
        WIN_FOCUS --> KEY_SIM[Simulador de Teclas\n(ex: Alt + Q + O)]
        KEY_SIM --> LOG_CMD[Log Objetivo: Comando Executado]
    end

    subgraph "Destino Metrológico (MeasurLink)"
        COM_OUT --> ML_SERIAL[MeasurLink Serial Input]
        KEY_SIM --> ML_WINDOW[MeasurLink Janela em Foco\n(Recebe Teclas Físicas)]
    end

    subgraph "Esteira Principal (Totalmente Isolada)"
        FW[FileSystemWatcher / QIF] --> QIF_PARSE[QifParser]
        QIF_PARSE --> PLC_WH[Siemens S7 / Webhook]
    end
```

---

## 🧩 3. Componentes da Solução por Camada

### 3.1 Camada de Domínio (`ConnectML.Core`)

#### Modelos de Domínio:
- **`Models/BarcodeRuleConfig.cs`**:
  Representa o vínculo entre a palavra-chave e o comando:
  ```csharp
  public class BarcodeRuleConfig
  {
      public string Keyword { get; set; } = string.Empty;
      public string CommandId { get; set; } = string.Empty;
  }
  ```

- **`Models/BarcodeCommandDefinition.cs`**:
  Representa a definição técnica de um comando customizável via JSON:
  ```csharp
  public class BarcodeCommandDefinition
  {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
      public string KeySequence { get; set; } = string.Empty; // ex: "%qo" para Alt+Q+O
      public string TargetWindowTitle { get; set; } = "MeasurLink";
      public int PreDelayMs { get; set; } = 80; // Pausa pós-foco para estabilização
  }
  ```

- **`Models/BarcodeTransportResult.cs`**:
  Objeto imutável de resultado contendo:
  - `RawData`: Conteúdo recebido.
  - `IsIntercepted`: Booleano se foi retido por regra.
  - `ExecutedCommandId`: ID do comando acionado (se interceptado).
  - `Success`: Veredito de execução/transporte.
  - `ErrorMessage`: Detalhe da falha, se houver.
  - `Timestamp`: Horário do evento.

#### Contratos de Serviço:
- **`Interfaces/IBarcodeCommandHandler.cs`**:
  ```csharp
  public interface IBarcodeCommandHandler
  {
      Task<bool> ExecuteCommandAsync(string commandId, string keyword);
      IReadOnlyList<BarcodeCommandDefinition> GetAvailableCommands();
      void ReloadCommands(IEnumerable<BarcodeCommandDefinition> commands);
  }
  ```

- **`Interfaces/IBarcodeMonitorService.cs`**:
  Contrato que expõe métodos de controle de ciclo de vida (`StartMonitoringAsync`, `StopMonitoringAsync`, `IsRunning`) e atualização dinâmica de regras.

### 3.2 Camada de Infraestrutura (`ConnectML.Infrastructure`)

- **`Win32/WindowFocusHelper.cs`**:
  - Encapsula chamadas de interop P/Invoke para localização segura de janelas no Windows:
    - `EnumWindows` + `GetWindowText` para encontrar janelas cujo título contenha `TargetWindowTitle` (ex: `"MeasurLink"`).
    - `IsIconic` e `ShowWindow(hWnd, SW_RESTORE)` se a janela estiver minimizada.
    - `SetForegroundWindow(hWnd)` com simulação de toque de tecla nula para contornar restrições de primeiro plano do Windows.
- **`Commands/MeasurLinkKeyboardCommandHandler.cs`**:
  - Implementa `IBarcodeCommandHandler`.
  - Executa a sequência:
    1. Localiza a janela-alvo (`WindowFocusHelper.FindAndFocusWindow`).
    2. Aguarda o `PreDelayMs` para assegurar que o Windows transferiu o foco de entrada de teclado.
    3. Simula as teclas configuradas no padrão do `SendKeys` (ex: `"%{q}{o}"` ou envio sequencial de `VK_MENU`, `Q`, `O`).
- **`Services/BarcodeSerialMonitorService.cs`**:
  - Leitura contínua orientada a linhas (`\r`, `\n` ou `\r\n`) em background thread.
  - Se coincidir com `Keyword`: aciona `IBarcodeCommandHandler` e **não despacha** para a COM de saída.
  - Se for leitura normal: despacha para a porta serial virtual de saída.
  - Tratamento resiliente de desconexão e erros de I/O.

### 3.3 Camada de Apresentação (`ConnectML.UI`)

- **XAML (`MainWindow.xaml`)**:
  - Novo card expansível **"Utilidades"**, logo abaixo de "Integração".
  - Subseção **"Leitor de Código de Barras"**:
    - Combobox `CmbBarcodeReaderPort` (Porta Serial do Leitor).
    - Combobox `CmbBarcodeOutputPort` (Porta Serial para o MeasurLink).
    - Subpainel dinâmico de **Palavras-chave e Comandos**:
      - Botão `+` (`BtnAddBarcodeRule`) para adicionar novas linhas.
      - `ItemsControl` contendo `TextBox` (Palavra-chave), `ComboBox` (Comando) e botão `-` (`BtnRemoveBarcodeRule`) vermelho para exclusão.
      - As opções da ComboBox de comandos são preenchidas com os comandos carregados da lista `AvailableCommands` do `AppConfig`.
    - Toggle button estilizado para ligar/desligar a comunicação serial do leitor.
- **Code-Behind & MVVM (`MainWindow.xaml.cs`)**:
  - `ObservableCollection<BarcodeRuleItem>` para vinculação dinâmica das regras na UI.
  - Ao abrir os dropdowns de portas seriais (`DropDownOpened`), lista portas COM disponíveis via `SerialPort.GetPortNames()`.
  - No `StartService()`, se o toggle do leitor estiver ativo, o monitor serial é disparado em background concorrente.
  - Ao clicar no Toggle Button em runtime, liga/desliga a escuta serial sem parar o serviço principal.
- **Configuração e Persistência (`AppConfig.cs`)**:
  - Novos campos em `%AppData%\ConnectML\appsettings.json`:
    - `BarcodeReaderEnabled` (bool)
    - `BarcodeReaderPort` (string)
    - `BarcodeOutputPort` (string)
    - `BarcodeRules` (`List<BarcodeRuleConfig>`)
    - `BarcodeCommands` (`List<BarcodeCommandDefinition>`): Contendo por padrão o comando `"Desfazer"` (`Id: "undo"`, `KeySequence: "%{q}{o}"`, `TargetWindowTitle: "MeasurLink"`).

---

## 🪵 4. Padrão de Logging Objetivo (Serilog e Aba Visual)

| Cenário | Entrada no Log (Arquivo e Visual) | Nível Serilog |
| :--- | :--- | :---: |
| **Transporte Normal** | `[Leitor] Leitura transportada para COM4: 'LOT-98231' [Sucesso]` | `Information` |
| **Comando Executado** | `[Leitor] Palavra-chave 'RESET' interceptada -> Comando 'Desfazer' (Alt+Q+O) executado no MeasurLink [Sucesso]` | `Information` |
| **Janela Não Encontrada** | `[Leitor] Palavra-chave 'RESET' interceptada -> Falha: Janela do 'MeasurLink' não encontrada [Erro]` | `Warning` |
| **Falha de Escrita COM** | `[Leitor] Falha ao despachar para COM4: A porta serial está inacessível.` | `Error` |

---

## 🛡️ 5. Análise de Riscos e Estratégia de Mitigação

| Risco | Impacto | Estratégia de Mitigação |
| :--- | :---: | :--- |
| **MeasurLink não receber teclas por falta de foco** | Alto | O `WindowFocusHelper` utiliza `AttachThreadInput` e validação com `GetForegroundWindow`. Adicionalmente, aplica um atraso configurável (`PreDelayMs`) de 50-100ms para garantir que a janela receptora processou a mensagem de foco antes de despachar as teclas. |
| **Comandos inválidos configurados no JSON** | Médio | Validação da sintaxe dos comandos no carregamento do `AppConfig`. Se um comando estiver com sequência vazia, o ConnectML emite aviso no log e não quebra a inicialização. |
| **Concorrência com Processamento QIF** | Crítico | A execução da automação de teclado e a leitura de portas seriais ocorrem em tarefas assíncronas dedicadas (`Task.Run`), sem concorrer com a fila de arquivos QIF ou com a thread de interface WPF. |

---

## 🗺️ 6. Roteiro Geral de Sprints (Fase 6 - v1.3.1)

```text
feature/barcode-serial-monitor
│
├── Sprint 6.1: Setup, Branch Git, Versionamento Global e Modelo JSON Extensível
├── Sprint 6.2: Arquitetura Core, Foco Win32 e Automação de Teclado no MeasurLink
├── Sprint 6.3: Interface do Usuário (UI) - Seção Utilidades e Regras Dinâmicas
├── Sprint 6.4: Integração de Ciclo de Vida, Não-Bloqueio e Logging Objetivo
└── Sprint 6.5: Validação com com0com, Testes de Foco/Teclas e Atualização Documental
```
