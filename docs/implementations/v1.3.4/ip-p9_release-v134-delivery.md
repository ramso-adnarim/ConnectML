# Relatório de Entrega: Fase 9 - Release e Deployment v1.3.4
**Aprimoramentos do HUD, Hit-Testing Geométrico, Estado Interrompido e Rastreabilidade QIF com Número de Série**

---

## 1. Visão Geral da Entrega
A **Versão 1.3.4** do ConnectML introduz uma série de refinamentos visuais, ergonômicos e funcionais demandados pela operação em chão de fábrica:
1. **Rastreabilidade e Identificação de Peça no QIF**: Extração resiliente da tag `<SerialNumber>` correlacionada à medição mais recente no arquivo QIF, apresentando a notificação no HUD sob a sintaxe unificada `<ROTINA> / <SERIALNUMBER>` (ex: `MANPESADO / 101807776895` ou `D-1001 / 123456`) e sem prefixos, enquanto a comunicação com o CLP Siemens S7 (`WriteStringAsync`) e Webhooks mantém estritamente apenas a rotina (`result.Product`), sem impacto em sistemas legados.
2. **Calibração de Hit-Testing Geométrico (Faixa Estrita de 8px)**: Limitação da captura de redimensionamento da borda luminosa a uma faixa centralizada de exatamente 8px (±4px sobre a aresta interna da borda), eliminando disparos acidentais do cursor `SizeNS`/`SizeWE` no meio de bordas espessas ou no interior da tela.
3. **Ancoragem Ergonômica do Corner Resize Grip**: Reposicionamento do ícone diagonal de redimensionamento (`TabResizeGrip`) diretamente no canto inferior direito da aba (ou respectivo canto para cada docking edge), acompanhando a aresta externa do contêiner.
4. **Desacoplamento de Escala dos Controles da Aba**: Preservação de dimensões ergonômicas estáveis (~14px para ícones, 11pt para o texto "Abrir") para os botões de ação e manipuladores quando a tipografia principal é aumentada (11 a 60pt), incluindo redução responsiva com clamp para fontes ultra-reduzidas (<14pt) e espaçamento amplo entre o título e as ações.
5. **Estado Visual "PROCESSAMENTO INTERROMPIDO" (#64748B)**: Estado sólido em cinza ardósia sem pulsação que é ativado quando o ConnectML é minimizado para a bandeja sem que o serviço de monitoramento esteja rodando (`!_isRunning`), sincronizado automaticamente com as operações de iniciar e parar o serviço.

---

## 2. Detalhamento dos Componentes Alterados

### 2.1. Extração de Número de Série no `QifParser.cs`
- **Arquivo**: `ConnectML.Core/QifParser.cs`
- **Modificações**:
  - Nova assinatura da tupla de retorno: `(bool IsOk, int FailCount, string Product, string SerialNumber)`.
  - Localiza o nó da medição mais recente (`latestMeasurement`), extrai o identificador em `<ActualComponentIds><Id>` e navega até o `<ActualComponent id="...">` correspondente em `<ActualComponentSets>`, extraindo o `<SerialNumber>`.
  - Implementa fallback em dois níveis: se a correlação falhar, busca o último componente com número de série no documento ou retorna string vazia em arquivos legados.
  - Testado e validado contra arquivos reais de chão de fábrica (`MANPESADO_20261024_130323.QIF` -> `"101807776895"`, `QIF-BOM.QIF` -> `"152"`).

### 2.2. Hit-Testing e Reposicionamento de Grips em `OverlayWidgetWindow`
- **Arquivos**: `ConnectML.UI/OverlayWidgetWindow.xaml`, `OverlayWidgetWindow.xaml.cs`
- **Modificações**:
  - `UpdateBorderResizeStrips()`: tiras perimetrais com espessura fixa de 8px e margem calculada via `offset = Math.Max(0, _borderThickness - 4)`, garantindo que apenas a aresta da borda capture o mouse.
  - `OverlayTabContainer.Padding = new Thickness(0)` para que o contêiner e suas tiras de resize ocupem 100% dos limites reais da aba.
  - `TabResizeGrip` extraído do painel sequencial e ancorado no canto inferior direito da aba (`HorizontalAlignment="Right"`, `VerticalAlignment="Bottom"`).
  - Tira longitudinal `TabEdgeResizeStrip` alinhada com a margem negativa da borda externa.

### 2.3. Tipografia Desacoplada e Espaçamento de Ações
- **Arquivo**: `ConnectML.UI/OverlayWidgetWindow.xaml.cs`
- **Modificações**:
  - Método `SetFontSize(double size)`:
    - `TxtOverlayStatus.FontSize` escala de 11 a 60pt.
    - `StateDot` escala com `_fontSize * 0.65`.
    - `IconSettingsViewbox`, `IconRestoreViewbox`, `GripViewbox`, `ResizeGripViewbox` e separadores operam fixos em ~14px (ou clamp entre 8 e 14px se fonte < 14pt).
    - `TxtBtnRestore.FontSize` opera em 11pt (ou clamp entre 8 e 11pt se fonte < 14pt).
    - Inserção de `titleGap = Math.Max(20, Math.Round(_fontSize * 0.85))` no primeiro separador e margem direita compacta para aproximar os botões da borda direita da aba sem aglomeração.

### 2.4. Estado "PROCESSAMENTO INTERROMPIDO" e Sincronização de Ciclo
- **Arquivos**: `ConnectML.UI/OverlayWidgetWindow.xaml.cs`, `ConnectML.UI/MainWindow.xaml.cs`
- **Modificações**:
  - Adicionado `GrayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"))`.
  - Implementado `SetInterruptedState()`: para a animação de pulso, cancela timers ativos, fixa opacidade em 1.0 e define cor cinza sólido com o texto `"PROCESSAMENTO INTERROMPIDO"`.
  - Método `ResetLifecycleState(bool toWaiting = true)` permite transicionar para waiting ou interrupted de forma centralizada.
  - Em `MainWindow.BtnMinimize_Click`: se `_isRunning`, ativa `SetWaitingState()`; se `!_isRunning`, ativa `SetInterruptedState()`.
  - Em `MainWindow.StartService()`: se oculta na bandeja, comuta o HUD para `SetWaitingState()`.
  - Em `MainWindow.StopService()`: se oculta na bandeja, comuta o HUD para `SetInterruptedState()`.
  - Em `MainWindow.ProcessFile`: notificação no HUD disparada com `hudMessage = $"{result.Product} / {result.SerialNumber}"` (ou `result.Product` se serial vazio), mantendo `_plcDriver.WriteStringAsync` intocado enviando apenas `result.Product`.

---

## 3. Unificação de Versionamento (v1.3.4)
- **`ConnectML.Core/ConnectML.Core.csproj`**: `<Version>1.3.4</Version>`
- **`ConnectML.Infrastructure/ConnectML.Infrastructure.csproj`**: `<Version>1.3.4</Version>`
- **`ConnectML.Simulator/ConnectML.Simulator.csproj`**: `<Version>1.3.4</Version>`
- **`ConnectML.UI/ConnectML.UI.csproj`**: `<Version>1.3.4</Version>`
- **`ConnectML.UI/Program.cs`**: `FindWindow(null, "ConnectML - V1.3.4")`
- **`ConnectML.UI/MainWindow.xaml`**: `Title="ConnectML - V1.3.4"` e `Versão 1.3.4`

---

## 4. Testes e Validação Integrada
- **Compilação da Solução**: `dotnet build ConnectML.sln` executado com êxito (0 erros).
- **Extração QIF Real**:
  - `C:\Mitutoyo\ConnectML\MANPESADO_20261024_130323.QIF` -> Veredito `PASS`, Rotina `MANPESADO`, Serial `101807776895`.
  - `C:\Mitutoyo\ConnectML\QIF-BOM.QIF` -> Veredito `PASS`, Rotina `D-1001`, Serial `152`.
  - `C:\Mitutoyo\ConnectML\QIF-FALHA.QIF` -> Veredito `FAIL`, Rotina `D-1001`, Serial `152`.
- **Validação de Ciclo de Vida**:
  - Minimização com serviço parado -> HUD em cinza sólido `"PROCESSAMENTO INTERROMPIDO"`.
  - Início do serviço com janela oculta -> HUD em amarelo pulsante `"AGUARDANDO MEDIÇÃO"`.
  - Processamento de arquivo -> HUD em verde sólido `"MANPESADO / 101807776895"` pelo tempo de tela (dwell).
  - Parada do serviço com janela oculta -> HUD comuta de volta para cinza sólido `"PROCESSAMENTO INTERROMPIDO"`.
