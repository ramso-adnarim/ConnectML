# Plano de Desenvolvimento: Sprint 3 - Fluxo Inbound (Minimal APIs e Despacho)

## 1. Visão Geral e Objetivo
A Etapa 3 ("Fluxo Inbound") tem como alvo transicionar o **ConnectML** para aceitar comandos entrantes nativos através de uma interface de rede HTTP moderna (Webhooks Genéricos). 
O escopo define a estruturação, subida do servidor e parsing (roteamento), para permitir que os sistemas terceiros do cliente acionem o processo de metrologia indiretamente via portas **Seriais Virtuais** com o *MeasurLink*.

Para suportar esse fluxo de forma madura e respeitando as restrições da Thread Principal da UI em WPF (Modo STA), adotaremos:
1. Servidor Kestrel orquestrado em background através de botões reais (Ligar/Desligar).
2. Endpoints estruturados usando o modelo .NET 8 Minimal API.
3. Abstração e despacho local na ferramenta nativa via _Virtual COM Ports_.

## 2. Escopo Arquitetural e Decisões de Design (Arquitetura Proposta)

### A. Lifecycle do Servidor Kestrel (Hospedagem)
Atualmente na Fase 1/2 o Kestrel fora inserido via `.ConfigureWebHostDefaults()` logo no início da subida do container global (`App.xaml.cs`) rodando na porta fixa vazada `5000`. Isso traz um risco de conflito na UI se a porta estiver em uso inviabilizando a abertura de tela, a porta local não acompanha alterações da UI pós-carregado.
Nesta Sprint, passaremos a interagir o `IHost` de forma amarrada botões ("Iniciar").
- **Ligamento (Binding):** Recuperaremos `Config.InboundPort` carregada no JSON e mapearemos o Kestrel.
- **Desligamento:** O comando `_host.StopAsync()` interrompe as portas quando o Workflow parar.

### B. Minimal APIs (Endpoints)
Os Endpoints Minimal APIs abstraem controladores pesados e melhoram incrivelmente a performance e alocamento em memória no contexto Híbrido, garantindo zero degradação na fluidez da tela (WPF).
- **Rota Inbound Principal:** Existirá o listener genérico `POST /api/webhooks/incoming`.
- **Validação:** Mapeamento em classes estáticas via Extensions como `MapWebhookEndpoints()`.

### C. Despacho (COM Virtual Interface)
Uma vez processada, a rota Webhook invoca um sistema Despachante focado em hardware:
- **`VirtualComDispatcher : IInboundDispatcher`**: Encapsulado na camada Core/Infrastructure; Recebe a mensagem serializada em formato limpo de comando e escreve (`SerialPort.WriteLine`) contra a Repasse da porta COM mapeada em `Config.VirtualComPort`.

## 3. Lista de Tarefas por Componente (Execution Steps)

#### 1. Camada UI (MainWindow e App.xaml.cs)
- [ ] Transferir ou encapsular a configuração builder do .NET Container (`InitializeKestrelHostAsync`) transferida para os fluxos Start/Stop do MainWindow.
- [ ] Recuperar parâmetros atualizados (`InboundPort`, `VirtualComPort`) da memória carregada.
- [ ] Vincular serviços globais (Serilog UI Sink) na pipeline de Injeção de Dependências.

#### 2. Endpoints em WebhookEndpoints.cs
- [ ] Criar namespace e classe `ConnectML.UI.Endpoints.WebhookEndpoints`.
- [ ] Assinar a Minimal Route do verbo POST apontando as requisições para a Action Local que invoca Log em UI avisando a recepção.
- [ ] Extrair Payload. Neste primeiro ciclo do Fluxo Inbound não precisaremos de Validação Segura ou HMAC (Mapeado só para a Sprint 5).

#### 3. Integração de Despacho e Equipamento Local
- [ ] Criar Modelos da Arquitetura para abstrair o comando: Criar interface `IInboundDispatcher`.
- [ ] Criar classe de concretização `VirtualComDispatcher`.
- [ ] Implementar a abertura e escrita da `System.IO.Ports.SerialPort` respeitando exceções se a porta estiver ocupada, propagando alerta gráfico se houver erro (Thread Dispatcher).

## 4. Critérios de Sucessos e Prevenção de Regressão Automática (Verification Plan)
Nenhuma quebra ao motor existente do driver nativo _Siemens S7 Driver_. O Inbound Fluxo deve coexistir com as execuções isoladas pelo ComboBox de Perfis.
Os testes serão finalizados validando:
1. Acionamento no botão Start da interface e status no visor virando _Verde_" (Iniciado com o Webhook Inbound Port XXXX escalado).
2. O envio de requisição pura via HTTP em JSON usando um Postman com o roteamento para localhost:porta.
3. Evento brilhando de volta visualmente e reativamente com a notificação da _chegada ao Despachador Virtual COM_ ativada pela Console.

---

> Esse documento age como fundação e bússola da Sprint 3. Ao autorizar sua implementação o modelo proposto avança para codificação.
