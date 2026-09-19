# AI Codebase Map: ConnectML (Versão 1.3.2)

Este documento serve como um mapa de arquitetura e glossário de domínio projetado especificamente para agentes de IA que farão manutenção, refatoração ou extensão do **ConnectML**. O objetivo é prover contexto imediato sobre as regras de negócio essenciais, convenções de engenharia e as armadilhas (gotchas) arquiteturais da base de código.

---

## 1. Glossário de Domínio

- **ConnectML**: Middleware de manufatura que atua como ponte entre softwares de metrologia (MeasurLink/Mitutoyo) e controladores lógicos programáveis (PLCs Siemens S7) ou sistemas corporativos via Webhook HTTP.
- **QIF (Quality Information Framework)**: Formato de arquivo XML padronizado pelo consórcio DMSC utilizado para exportar os resultados das rotinas de medição de CMMs. O ConnectML consome estes arquivos para extrair o veredito da peça (`PASS`/`FAIL`) e dados dimensionais.
- **Inbound (Monitoramento de Disco)**: Rotina executada pelo `FileWatcherService`, que observa ativamente um diretório local ou de rede em busca de novos arquivos `.QIF` recém-gerados pelas máquinas de medir.
- **Parsing**: O processo (`QifParser`) que analisa o stream XML sob codificação Latin1, localiza o nó da medição mais recente e deduz o status da peça.
- **Outbound (Destino do Veredito)**:
  - **S7Comm (PLC)**: Escrita direta em blocos de dados (DB) da Siemens utilizando TCP/IP (ISO-on-TCP).
  - **Webhook (JSON)**: Despacho assíncrono via HTTP POST para endpoints configurados, com suporte a templates Liquid.
- **Barcode Serial Monitor (`BarcodeSerialMonitorService`)**: Serviço concorrente que monitora a porta serial de entrada do leitor de código de barras e transporta leituras em tempo real para a porta virtual do MeasurLink (`com0com`), interceptando palavras-chave cadastradas.
- **Automação MeasurLink / Simulador de Teclas (`MeasurLinkKeyboardCommandHandler`)**: Executor Win32 que localiza e foca a janela do MeasurLink para disparar atalhos físicos de teclado, como `Alt + F + O` (Desfazer medição).
- **WindowFocusHelper**: Utilitário P/Invoke para localização de janelas por correspondência de título, restauração de minimizado e concessão segura de foco de teclado via `AttachThreadInput`.
- **HUD / Overlay (`OverlayWidgetWindow`)**: Janela de sobreposição translúcida com borda periférica e aba informativa de alta visibilidade, operando em modo não-intrusivo para monitoramento no chão de fábrica.
- **Dwell Timer**: Temporizador de retenção visual (0.5s) conjugado ao HUD, que sustenta o veredito de aprovação/reprovação na tela antes de retomar o modo amarela de prontidão.
- **Snapping Magnético**: Mecanismo de encaixe da aba de status nas 4 extremidades da tela (`TOP`, `BOTTOM`, `LEFT`, `RIGHT`) calculado contra a `SystemParameters.WorkArea`.
- **Simulator (`ConnectML.Simulator`)**: Console app auxiliar que emula a porta 102 TCP e responde ao handshake S7Comm para testes sem hardware físico.
- **Velopack**: Gerenciador de ciclo de vida e distribuição para Windows que viabiliza auto-updates via GitHub Releases usando pacotes diferenciais delta.

---

## 2. Componentes Críticos e Camadas

```
c:\Antigravity\ConnectML
├── ConnectML.Core/
│   ├── Interfaces/IBarcodeCommandHandler.cs # Contrato para execução de atalhos/comandos
│   ├── Interfaces/IBarcodeMonitorService.cs  # Contrato do ciclo de vida do monitor serial
│   ├── Interfaces/IPlcDriver.cs             # Contrato de comunicação com PLC
│   ├── Models/BarcodeCommandDefinition.cs   # Definição de comandos customizáveis (JSON)
│   ├── Models/BarcodeRuleConfig.cs          # Mapeamento palavra-chave vs comando
│   ├── Models/BarcodeTransportResult.cs     # Veredito da leitura serial (normal vs interceptado)
│   ├── Models/InspectionResult.cs           # Veredito de medição (PASS/FAIL)
│   └── Parsers/QifParser.cs                 # Parser XML tolerante em Latin1
├── ConnectML.Infrastructure/
│   ├── Commands/MeasurLinkKeyboardCommandHandler.cs # Simulação física de atalhos (ex: Alt+F+O)
│   ├── Interop/WindowFocusHelper.cs         # P/Invoke Win32 para busca e foco de janelas
│   ├── PlcDrivers/SiemensS7Driver.cs        # Driver S7 com normalização automática de DBs
│   ├── Services/BarcodeSerialMonitorService.cs # Monitor serial não-bloqueante orientado a linhas
│   ├── Services/FileWatcherService.cs       # Observador de disco com debounce e retry Polly
│   └── Logging/                             # Configuração Serilog (Rolling File e UI Sink)
├── ConnectML.UI/
│   ├── MainWindow.xaml / .cs                # View principal, Card Utilidades, orquestração de ciclo
│   ├── OverlayWidgetWindow.xaml / .cs       # Janela HUD com interop Win32, grips e snapping
│   ├── OverlaySettingsWindow.xaml / .cs     # Janela modal desacoplada para ajustes do HUD
│   ├── Models/AppConfig.cs                  # Modelo de configurações persistidas (inclui Leitor/JSON)
│   └── Program.cs                           # Entrypoint com Mutex de instância única e VelopackApp.Run()
└── ConnectML.Simulator/
    └── Program.cs                           # Socket TCP 102 simulando COTP + S7 Setup
```

---

## 3. Regras de Ouro e "Gotchas" (Importante para Modelos de IA)

Para evitar quebras de regressão, falhas de concorrência ou comportamentos indesejados no chão de fábrica, siga rigorosamente estas regras ao manipular o código:

### 3.1. Concorrência e UI Thread (Cross-Thread Exceptions)
- **O Problema**: Eventos advindos de conexões TCP/IP (driver S7) ou monitoramento de disco (`FileWatcherService`) disparam em threads de background (ThreadPool). O WPF lança `InvalidOperationException` se elementos visuais forem tocados fora da UI Thread.
- **A Regra**: **Sempre** utilize `Application.Current.Dispatcher.Invoke` (ou `Dispatcher.BeginInvoke`) ao atualizar propriedades de tela, disparar Storyboards ou instanciar janelas quando a ação tiver origem em serviços de I/O ou rede.

### 3.2. Não Roubar Foco com o HUD (`WS_EX_NOACTIVATE`)
- **O Problema**: O operador está interagindo com outros softwares (ex: MeasurLink ou AutoCAD). Se o HUD for ativado ou receber foco via `this.Activate()` ou `this.Focus()`, o teclado do operador será desativado na aplicação subjacente, causando transtornos operacionais.
- **A Regra**: O HUD deve manter permanentemente as flags Win32 `WS_EX_NOACTIVATE (0x08000000)` e `WS_EX_TOOLWINDOW (0x00000080)`. **Jamais** chame métodos que forcem a ativação da janela de overlay.

### 3.3. Área Útil de Tela (`SystemParameters.WorkArea`)
- **O Problema**: Computadores industriais frequentemente possuem a barra de tarefas do Windows configurada no topo, nas laterais ou em monitores com resoluções atípicas.
- **A Regra**: Para todo e qualquer cálculo de snapping, redimensionamento ou ancoragem da aba do HUD, utilize estritamente as dimensões de `SystemParameters.WorkArea` (WorkArea.Left, WorkArea.Top, WorkArea.Width, WorkArea.Height). **Nunca** utilize `SystemParameters.PrimaryScreenWidth` ou `PrimaryScreenHeight`.

### 3.4. Rotação Tipográfica em Painéis Verticais (`LEFT` e `RIGHT`)
- **O Problema**: Nos modos laterais, um texto horizontal simples ficaria truncado ou ilegível.
- **A Regra**: Ao ancorar nas bordas esquerda ou direita, o contêiner interno da aba aplica `RotateTransform Angle="90"`. A ordem visual dos elementos internos é invertida programaticamente para assegurar leitura ergonômica de baixo para cima. Qualquer novo elemento adicionado à aba deve contemplar ambos os estados (horizontal e vertical).

### 3.5. Limites Industriais de Escala (1..60 px e 11..60 pt)
- **O Problema**: Em chão de fábrica, operadores visualizam telas a mais de 5 metros de distância. Limites convencionais de desktop (ex: fontes até 14pt) tornam a interface inútil.
- **A Regra**: Mantenha sempre a faixa ultra-expandida de ajuste:
  - Espessura de Borda: de `1` a `60` pixels.
  - Tamanho da Aba e Tipografia: de `11` a `60` pontos.

### 3.6. Dwell Timer e Transição de Estados
- **O Problema**: Após uma medição, a aplicação retorna imediatamente para o modo de espera, o que pode fazer com que o operador perca o piscar de veredito da peça.
- **A Regra**: Todo evento de medição concluída deve congelar a tela pelo intervalo exato de **0.5 segundos** (Dwell Timer) no estado `PASS` (Verde) ou `FAIL` (Vermelho) antes de acionar a transição de volta ao modo `Aguardando Medição` com a cor **Safety Yellow** (`#FFCC00`) e animação rápida de pulso (0.5s / 1 Hz).

### 3.7. Persistência Isolada em `%AppData%\ConnectML\appsettings.json`
- **O Problema**: Ao aplicar uma atualização via Velopack, a pasta de execução (`app-1.x.x`) é recriada do zero com os binários de release e um `appsettings.json` padrão de fábrica. Se a aplicação lesse ou gravasse configurações localmente no diretório do executável, ou se a rotina de migração aceitasse o template padrão recente de novas pastas `app-*`, os dados customizados do cliente seriam destruídos no update.
- **A Regra**: As configurações de tempo de execução são armazenadas e lidas exclusivamente em `%AppData%\ConnectML\appsettings.json`. O método `EnsureSettingsMigrated()` implementa duas salvaguardas invioláveis:
  1. Se `%AppData%\ConnectML\appsettings.json` já existir e for customizado (`!IsDefaultTemplateConfig`), ele **nunca** é sobrescrito.
  2. Ao migrar de instalações legadas (ex: v1.2.0), pastas de versões iguais ou superiores à atual são ignoradas e templates de instalação são discriminados para recuperar as credenciais reais do cliente.

### 3.8. Normalização de Endereços Siemens S7
- **O Problema**: Operadores digitam na interface notações encurtadas como `DB10.0` ou `DB20.2`. A biblioteca `S7NetPlus` exige notações formais do protocolo sob pena de `ArgumentOutOfRangeException`.
- **A Regra**: Todas as strings de endereçamento inseridas passam pelo método de normalização do `SiemensS7Driver`, expandindo bits para `DB10.DBX0.0` e palavras para `DB10.DBW2`.

### 3.9. Encoding Latin1 em Arquivos QIF
- **O Problema**: Arquivos XML de relatórios de metrologia contêm símbolos físicos como graus (`°`), diâmetro (`Ø`) e caracteres especiais. O parsing em UTF-8 puro pode truncar ou lançar exceções de caractere inválido.
- **A Regra**: A leitura de streams e buffers de arquivos `.qif` deve ser realizada com `Encoding.Latin1` (ISO-8859-1).

### 3.10. Foco Win32 em Aplicações Alvo (Simulação de Teclas)
- **O Problema**: O envio de teclas simuladas via `keybd_event` ou `SendInput` exige que a aplicação de destino (ex: MeasurLink) seja a janela ativa de primeiro plano. Se a janela não estiver em foco ou se a chamada ocorrer antes da estabilização do Windows, as teclas são perdidas ou disparadas na janela errada. Além disso, pastas abertas no Windows Explorer contendo "MeasurLink" no título podem ser falsamente ativadas.
- **A Regra**: 
  1. O `WindowFocusHelper` utiliza algoritmo de pontuação de processos (`GetWindowThreadProcessId` + `Process.GetProcessById`), concedendo pontuação máxima (+10.000 pontos) para executáveis genuínos do MeasurLink (`Mitutoyo.MeasurLink.WinConsole.exe`, `DataCollection.exe`) e descartando instâncias do `explorer.exe`.
  2. A janela é restaurada se minimizada (`ShowWindow SW_RESTORE`) e ganha foco com privilégios via `AttachThreadInput`.
  3. Respeite o atraso mínimo (`PreDelayMs`, padrão 150ms) antes do disparo de teclas.
  4. Para sequências com tecla `Alt` (ex: `Alt + F + O`), utilize códigos de varredura de hardware OEM (`MapVirtualKey` com `KEYEVENTF_SCANCODE`) e respeite a cadência de mnemônicos do Windows: pressione `Alt`, envie a tecla do menu (`F`), solte `Alt`, aguarde 180ms para abertura do menu e envie a tecla da ação (`O`).

### 3.11. Não-Bloqueio da Esteira Principal pelo Leitor Serial
- **O Problema**: Falhas na porta serial (desconexão física de cabos USB ou erros de paridade) podem lançar exceções de I/O bloqueantes.
- **A Regra**: Todo o monitoramento do leitor de código de barras deve ser contido dentro de tarefas assíncronas isoladas (`Task.Run`), com captura segura de exceções e cancelamento cooperativo. Uma pane na porta COM jamais pode travar o `FileSystemWatcher` ou o envio aos PLCs.

### 3.12. Sincronização e Hot-Reload de Configurações (`appsettings.json`)
- **O Problema**: Edições manuais no arquivo de configuração do usuário (`%AppData%\ConnectML\appsettings.json`) ou via botão da interface (`BtnOpenConfigJson`) poderiam não surtir efeito imediato se a aplicação mantivesse uma cópia estática em memória.
- **A Regra**: A aplicação deve recarregar a lista `BarcodeCommands` do disco através de `ReloadBarcodeCommandsFromDisk()` sempre que o serviço for iniciado, parado, o switch do leitor for alternado, uma nova regra for adicionada ou a janela recuperar o foco do sistema operacional (`Window.Activated`).

### 3.13. Inicialização Assíncrona e Ativação Universal do HUD no Startup
- **O Problema**: No startup automático do Windows, a chamada de inicialização do serviço (`StartService`) é assíncrona devido ao handshake de rede TCP com o CLP (`await _plcDriver.ConnectAsync`). Se a minimização para a bandeja verificar `if (_isRunning)`, a flag ainda estará como `false` no exato instante do minimize, fazendo com que o HUD não apareça na tela.
- **A Regra**: O método `BtnMinimize_Click` deve invocar `_overlayWindow.Show()` **sempre** que a janela principal for ocultada para a bandeja, sem travas de estado de execução. Adicionalmente, o reinício automático em `Window_Loaded` exige mandatoriamente que a checkbox `ChkAutoStart.IsChecked == true` esteja marcada pelo usuário; a mera existência de `WasServiceRunning` jamais deve disparar a inicialização sem o consentimento da checkbox.

