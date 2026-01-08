ConnectML (MeasurLink Connector)

ConnectML é um middleware industrial desenvolvido para integrar dados de qualidade do MeasurLink (QIF) com sistemas de automação de fábrica (CLPs e SCADAs).

Funcionalidade Principal

Monitoramento: Observa diretórios configurados em busca de arquivos .qif (XML) gerados pelo MeasurLink.

Processamento: Analisa o XML em tempo real, extraindo a última inspeção e validando tolerâncias.

Comunicação (OT): Envia os resultados (Aprovado/Reprovado ou Contagem de Falhas) para o CLP via protocolo industrial.

Estrutura do Projeto

A solução segue uma arquitetura modular em camadas (.NET 8):

ConnectML.Core: O coração da aplicação. Contém Interfaces (IPlcDriver), Modelos (InspectionResult) e lógica agnóstica de tecnologia.

ConnectML.Infrastructure: A camada de "peso pesado". Implementa os drivers de comunicação (S7NetPlus), logs (Serilog) e acesso a arquivos.

ConnectML.UI: Aplicação Desktop WPF moderna, focada em simplicidade operacional.

Tecnologias

Framework: .NET 8 (LTS)

UI: WPF + MVVM (CommunityToolkit) + Estilo Industrial Dark

Logging: Serilog

Drivers PLC:

Siemens S7: Implementado (via S7NetPlus)

OPC UA: (Roadmap)

MQTT: (Roadmap)

Instalação e Desenvolvimento

Pré-requisitos: Visual Studio 2022, .NET 8 SDK.

Setup: Execute o script init_connectml.ps1 no PowerShell para criar a estrutura e baixar dependências.

Execução: Abra ConnectML.sln, defina ConnectML.UI como projeto de inicialização e execute.

Roadmap

[x] Definição de Arquitetura e Nomeação

[x] Driver Siemens S7 (Base)

[ ] Dashboard UI (WPF/XAML)

[ ] Processador de XML QIF (LINQ)

[ ] Serviço de Monitoramento de Diretório