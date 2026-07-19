# ![ConnectML Logo](/ConnectML-logo-ico.ico) 
# ConnectML

ConnectML é um middleware industrial desenvolvido para integrar dados de qualidade do MeasurLink (QIF) com sistemas de automação de fábrica (CLPs e SCADAs) e outros softwares de apontamento e integração.

## Documentação

Os arquivos de documentação do projeto podem ser encontrados na pasta `docs`. O sumário inclui:
* [ARCHITECTURE.md](docs/ARCHITECTURE.md): Visão geral da arquitetura, fluxo de dados e decisões técnicas da aplicação.
* [AI_CODEBASE_MAP.md](docs/AI_CODEBASE_MAP.md): Mapa da codebase, glossário de domínio e regras vitais de negócio e estrutura.
* [DEPLOYMENT_VELOPACK.md](docs/DEPLOYMENT_VELOPACK.md): Guia de empacotamento e distribuição utilizando Velopack.
* Documentação de desenvolvimento e ciclo de vida: Os planejamentos e histórico estão localizados em `docs/implementations/`.

## Áreas do Projeto

A solução segue uma arquitetura modular em camadas (.NET 8):

* **ConnectML.Core:** O coração da aplicação. Contém Interfaces, Modelos e lógica agnóstica de tecnologia.
* **ConnectML.Infrastructure:** A camada de "peso pesado". Implementa os drivers de comunicação (S7NetPlus, API HTTP, etc), logs (Serilog) e acesso a arquivos.
* **ConnectML.UI:** Aplicação Desktop WPF moderna, focada em simplicidade operacional.
* **ConnectML.Simulator:** (Opcional) Ferramentas para simular os dispositivos de integração.

## Funcionalidade Principal (Status Atual)

* **Monitoramento:** Observa diretórios configurados em busca de arquivos `.qif` (XML) gerados pelo MeasurLink.
* **Processamento:** Analisa o XML em tempo real, extraindo a última inspeção e validando tolerâncias.
* **Comunicação (OT):** Envia os resultados para o CLP via protocolo industrial.

## Tecnologias

* **Framework:** .NET 8 (LTS)
* **UI:** WPF + MVVM (CommunityToolkit) + Estilo Industrial Dark
* **Logging:** Serilog
* **Drivers e Integrações:**
  * Siemens S7: Implementado (via S7NetPlus)
  * EGA (API REST): *Em Desenvolvimento*
  * OPC UA: (Roadmap)
  * MQTT: (Roadmap)

## Instalação e Desenvolvimento

**Pré-requisitos:** Visual Studio 2022, .NET 8 SDK.

**Setup:** Execute o script `init_connectml.ps1` no PowerShell para criar a estrutura e baixar dependências.

**Execução:** Abra `ConnectML.sln`, defina `ConnectML.UI` como projeto de inicialização e execute.