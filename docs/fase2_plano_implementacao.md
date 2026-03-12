# Plano de Implementação - Fase 2 (Expansão de Integrações: EGA API)

Este documento detalha o plano de ação e as tarefas para expandir os mecanismos de integração do ConnectML, implementando a comunicação bidirecional via API REST (JSON) e repasse de comandos para o MeasurLink.

## 1. Revisão da Estrutura do Projeto para Aderência com API

**Situação Atual:**
O ConnectML está estruturado como uma aplicação WPF Desktop (`ConnectML.UI`), com a infraestrutura separada em `ConnectML.Infrastructure` e regras de negócio em `ConnectML.Core`. O projeto usa `.NET 8`.
Atualmente, a aplicação atua como um sistema unidirecional e não possui um servidor web embutido para escutar requisições de outras aplicações (Endpoints).

**O que deve ser feito:**
* **Adicionar Host Web:** Precisamos adicionar um serviço de hospedagem web (ex: componente `Microsoft.AspNetCore` via Minimal APIs ou `HttpListener`) rodando em background junto com a interface WPF. Isso permitirá que a aplicação escute na porta especificada e receba os gatilhos JSON da EGA.
* **Injeção de Dependências Dinâmica:** Como a aplicação suportará múltiplas integrações simultâneas ou alternadas (Siemens S7, EGA), a arquitetura do Core deve ser estendida para suportar Múltiplos Handlers (Interfaces de Gatilhos Externos).
* **Motor Bidirecional:** Um novo fluxo deve ser construído na Engine do ConnectML: *Listener (API) -> Core Engine -> Repasse MeasurLink (Serial Virtual)*. O fluxo atual de subida de dados permanecerá: *Extrator QIF -> Core Engine -> Handler Envio (EGA API Client / S7NetPlus)*.

## 2. Mockup da UI e Parametrização

*Foi adicionado ao `MainWindow.xaml` uma representação visual para a configuração EGA.*
As seguintes informações foram contempladas na UI e devem ser persistentes no `appsettings.json`:
* **Protocolo de Integração:** EGA (API REST / JSON).
* **Configurações da API Local:** Endpoint URL Base e Porta de escuta (onde o ConnectML aguardará comandos).
* **Fluxo: Receber da API (Gatilhos):** Opção para habilitar o comando Iniciar (GO), que embutirá Nome da Rotina, Corrida e Tempo. Também foi adicionado um campo para especificar em qual 'Porta Serial Virtual' do MeasurLink este comando deve ser retransmitido.
* **Fluxo: Enviar para API (Resultados):** Opções com checagem para selecionar as variáveis de saída desejadas, como Status da Medição, Nome da Rotina, Nome da Corrida e Evento Estatístico Ocorrido.

## 3. Plano de Implementação Detalhado e Confirmações Faltantes

Para que as funcionalidades operem corretamente, precisamos preencher algumas lacunas e executar as validações a seguir antes ou durante a codificação:

### 3.1 Recebimento de Comando "GO" (API EGA -> ConnectML)
* **Visão Técnica:** Criar Endpoint interno `POST /api/trigger/go` recebendo um payload JSON.
* **Pendente Confirmações:**
  * A EGA atua como Cliente (fazendo POST no ConnectML) ou o ConnectML atua fazendo GET/Polling na EGA para buscar ordens novas? (Nosso modelo assume a EGA fazendo POST no ConnectML).
  * Haverá algum mecanismo de autenticação para as requisições, como Bearer Token, API Key estática no Header?
  * Confirmar a nomenclatura exata dos nós JSON desejados pela EGA (ex. será `runName` ou `OrdemProducao`?).

### 3.2 Repasse para o MeasurLink (ConnectML -> Serial Virtual)
* **Visão Técnica:** Usar classe `System.IO.Ports.SerialPort` contida no runtime do .NET para abrir comunicação COMx e escrever bytes no buffer.
* **Pendente Confirmações:**
  * Qual o protocolo/formato de texto exigido pelo MeasurLink na interface Serial? (Ex: string delimitada por vírgulas, terminador EOF como `\r\n` ou `CRLF`, formato `<GO>Rotina123|Corrida777`).
  * O MeasurLink retorna algum ACK/NACK via Serial para obtermos certeza do Início ou é uma submissão cega?

### 3.3 Envio de Medição Finalizada (ConnectML -> API EGA)
* **Visão Técnica:** Transformar os resultados extraídos do QIF em um modelo C# equivalente ao JSON de saída desejado e executar `HttpClient.PostAsync` para um Endpoint providenciado pelos mantenedores da EGA.
* **Pendente Confirmações:**
  * Qual o Endpoint oficial (URL da EGA) para onde faremos o upload dos resultados? E qual o verbo HTTP aceito (PUT, POST)?
  * "Evento Estatístico Ocorrido": O padrão QIF contém esse evento detalhadamente em algum nó específico ou o ConnectML terá que inferir isso programaticamente com base em regras parametrizadas? (Ex: Regra de tendência baseada nos valores lidos das características).

## Próximos Passos
1. Coletar respostas junto à equipe EGA e MeasurLink referentes às **Confirmações Faltantes**.
2. Criar ramo (branch) de *feature* para o desenvolvimento da API no backend.
3. Definir uso restrito ao `Microsoft.AspNetCore` (Kestrel embutido WPF) e conectar ao painel de UI (ViewModels).
4. Proceder com testes enviando JSON via `curl` ou Postman para ativar os canais de simulação (Mock).
