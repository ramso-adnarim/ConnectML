# ![ConnectML Logo](/ConnectML-logo-ico.ico) ConnectML

[![Release](https://img.shields.io/github/v/release/ramso-adnarim/ConnectML?color=0078D4&label=Vers%C3%A3o%20Atual)](https://github.com/ramso-adnarim/ConnectML/releases/tag/1.3.1)
[![Target](https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue.svg)](https://microsoft.com/windows)
[![Velopack](https://img.shields.io/badge/Updates-Velopack-success.svg)](https://velopack.io/)

**ConnectML** é um middleware industrial de alta performance desenvolvido para atuar como ponte em tempo real entre softwares de metrologia (**MeasurLink / Mitutoyo**) e sistemas de automação de manufatura (**CLPs Siemens S7**, barramentos industriais e endpoints **Webhook REST**).

O sistema monitora a geração de arquivos de inspeção **QIF (Quality Information Framework)**, extrai automaticamente os dados da última peça inspecionada, valida tolerâncias e despacha sinais de controle (Aprovado/Reprovado ou Contadores) para as linhas de montagem.

---

## 🌟 Novidades da Versão 1.3.1: Ponte Serial para Leitor de Código de Barras e Automação MeasurLink

Na versão **1.3.1**, o ConnectML introduz uma nova seção de **Utilidades** voltada à integração de leitores de código de barras:

* **Ponte Serial Transparente (com0com)**: Monitora a porta COM serial onde o leitor físico ou virtual está conectado e transporta as leituras automaticamente para uma porta serial de saída virtual (conectada ao MeasurLink) sem latência.
* **Intercepção Condicional de Comandos**: Permite cadastrar palavras-chave que não serão enviadas via serial para a medição, mas sim convertidas em comandos de automação.
* **Foco Win32 & Simulação de Teclas no MeasurLink**: Ao interceptar uma palavra-chave, o ConnectML localiza a janela do MeasurLink (`EnumWindows`), restaura-a e coloca-a em primeiro plano (`SetForegroundWindow`), simulando atalhos físicos de teclado como **`Alt + F + O`** para o comando **Desfazer**.
* **Comandos Customizáveis via JSON (`appsettings.json`)**: Novos comandos e combinações de teclas podem ser definidos ou personalizados diretamente no arquivo de configuração do usuário, com suporte a recarregamento dinâmico (hot-reload) a quente.
* **Acesso Rápido às Configurações**: Botão sutil integrado na seção Utilidades para abrir o arquivo `appsettings.json` ativo diretamente no editor padrão com um único clique.
* **Execução Paralela Não-Bloqueante**: O monitoramento das portas COM e os disparos de teclado operam 100% em tarefas assíncronas em segundo plano, garantindo zero interferência na esteira principal de arquivos QIF e na comunicação com PLCs.
* **Logs Objetivos**: Registro conciso no Serilog e exibição imediata na aba visual LOG informando o sucesso do transporte ou da execução do atalho.

---

## 📥 Download da Versão Mais Recente (v1.3.1)

A versão **1.3.1** está disponível com suporte a instalação limpa e atualizações automáticas silenciosas:

* 🚀 **[Baixar Instalador Windows (ConnectML-win-Setup.exe)](https://github.com/ramso-adnarim/ConnectML/releases/download/1.3.1/ConnectML-win-Setup.exe)** — *Instalação completa com atalhos no Desktop e Menu Iniciar*
* 📦 **[Baixar Pacote Portátil (ConnectML-win-Portable.zip)](https://github.com/ramso-adnarim/ConnectML/releases/download/1.3.1/ConnectML-win-Portable.zip)** — *Execução direta sem necessidade de instalação*
* 📋 **[Ver Notas da Release v1.3.1 no GitHub](https://github.com/ramso-adnarim/ConnectML/releases/tag/1.3.1)**

---

## 🌟 Versão 1.3.0: Widget Overlay HUD Industrial

Na versão 1.3.0, o ConnectML introduz o subsistema **Widget Overlay HUD (Heads-Up Display)**, projetado especificamente para operadores de chão de fábrica:

* **Janela Não-Intrusiva (`WS_EX_NOACTIVATE`)**: A sobreposição transparente permanece visível na tela sem roubar o foco do teclado ou mouse de aplicações em uso (ex: softwares de medição, CAD/CAM ou CNC) e oculta no `Alt+Tab`.
* **Acoplamento Magnético (4 Bordas)**: Permite ancorar a aba de status nas extremidades **Superior**, **Inferior**, **Esquerda** e **Direita** da tela (`WorkArea`).
* **Adaptação e Rotação Vertical**: Ao fixar nas laterais, a tipografia e ícones rotacionam 90° com orientação ergonômica de baixo para cima.
* **Janela de Configurações Desacoplada**: Painel exclusivo aberto pelo ícone de engrenagem (⚙️) na aba do HUD, mantendo a tela principal limpa.
* **Redimensionamento Direto via Mouse Drag**: Ajuste livre da espessura da borda e tamanho da aba arrastando diretamente suas extremidades com o cursor.
* **Limites Visuais Ultra-Expandidos**: Espessura de borda de **1 a 60 px** e fontes de **11 a 60 pt** para leitura nítida a longa distância.
* **Alerta Safety Yellow e Pulso Acelerado**: Realce imediato de prontidão em amarelo industrial forte (`#FFCC00`) com pulso de 0.5s (1 Hz).
* **Dwell Timer Conjugado (0.5s)**: Retenção visual temporizada do veredito (Aprovado em Verde / Reprovado em Vermelho) antes do retorno ao modo de prontidão.
* **Ativação Mandatória ao Minimizar**: O HUD assume a supervisão contínua da linha sempre que o ConnectML é minimizado para a bandeja do sistema.

---

## 📚 Documentação Técnica do Projeto

A documentação arquitetural e de engenharia está organizada na pasta `docs`:

* 🏛️ **[ARCHITECTURE.md](docs/ARCHITECTURE.md)**: Visão aprofundada da arquitetura em camadas, fluxo de dados, subsistema HUD e integração Win32/Velopack.
* 🤖 **[AI_CODEBASE_MAP.md](docs/AI_CODEBASE_MAP.md)**: Mapa conceitual da base de código, glossário de domínio e regras de ouro para desenvolvimento assistido por IA.
* 🚀 **[DEPLOYMENT_VELOPACK.md](docs/DEPLOYMENT_VELOPACK.md)**: Guia completo de compilação, empacotamento local e publicação de releases com pacotes delta no GitHub.
* 📝 **[Histórico de Implementações](docs/implementations/)**: Detalhamento dos planos de execução, especificações e relatórios de entrega de cada versão.

---

## 🧩 Estrutura da Solução (.NET 8)

| Projeto | Papel na Arquitetura |
| :--- | :--- |
| **`ConnectML.Core`** | Núcleo de domínio: Interfaces puras (`IPlcDriver`), modelos de medição e parser XML (`QifParser`) em codificação Latin1. |
| **`ConnectML.Infrastructure`** | Drivers de comunicação física (`SiemensS7Driver`), monitoramento de diretórios reativo (`FileWatcherService`) e logging estruturado (`Serilog`). |
| **`ConnectML.UI`** | Interface Desktop moderna em WPF (Dark Industrial), gerenciamento do HUD Overlay, ciclo de vida da bandeja e atualizações Velopack. |
| **`ConnectML.Simulator`** | Emulador local Socket TCP (porta 102) que simula o protocolo Siemens S7Comm para validações em ambiente de desenvolvimento. |

---

## 🛠️ Tecnologias e Protocolos

* **Framework Base**: .NET 8 (LTS)
* **Frontend**: WPF (XAML) + Interop Win32 (User32 / SetWindowLong)
* **Comunicação OT**: Siemens S7Comm (S7-300, S7-1200, S7-1500) via S7NetPlus
* **Comunicação TI**: Webhook HTTP POST com suporte a templates Liquid e assinatura HMAC-SHA256
* **Formato Metrológico**: Quality Information Framework (QIF / XML)
* **Distribuição**: Velopack (Instaladores autônomos e pacotes delta diferenciais)

---

## 💻 Instruções para Desenvolvedores

**Pré-requisitos**:
* Windows 10/11 x64
* .NET 8 SDK
* Visual Studio 2022 ou VS Code / Antigravity IDE

**Compilação via Terminal**:
```powershell
# Restaurar dependências e compilar a solução em Release
dotnet build ConnectML.sln -c Release

# Executar a aplicação principal
dotnet run --project ConnectML.UI\ConnectML.UI.csproj
```