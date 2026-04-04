# Plano de Desenvolvimento: Sprint 4 - Fluxo Outbound (Motor de Templates e HttpClient)

## 1. Visão Geral e Objetivo
A Etapa 4 ("Fluxo Outbound") é responsável por consolidar a ponte inversa. No mundo antigo, a aplicação lia um arquivo estruturado XML do MeasurLink (`.qif`) e os bit-flags eram repassados rigidamente via driver para uma CLP Siemens (S7). 

Nesta Sprint 4, implementaremos a capacidade da aplicação formatar esses mesmos dados através de um **Motor de Templates (Liquid)** configurável em tempo real na interface, encapsulando-os em um payload JSON personalizado pelo usuário. Logo após o processamento, em vez de endereçar uma porta de máquina, o sistema fará um *Post/Put* via HTTP (Webhook Dispatcher) para URL externa de sistemas web, integrando o ConnectML a nuvem.

## 2. Escopo Arquitetural e Decisões de Design (Arquitetura Proposta)

### A. Shaping Dinâmico com Liquid Templates
Usaremos o **Fluid.Core**, um interpretador do *Liquid Template Language* rápido e nativo do .NET. Isso evitará qualquer acoplamento de classes C# com a formatação final de JSON, ou seja, se o sistema destino requer `{"status_ok": true}` ou `{"Result": "PASS"}`, o cliente moldará o template XAML na aba UI `TxtPayloadTemplate` em tempo real. Exemplo:
```json
{
  "Device": "ConnectML",
  "Measurements": {{ FailCount }},
  "Approved": {{ IsOk }}
}
```

### B. Módulo de Despacho Outbound (IOutputDriver)
Assim como criamos o `IInboundDispatcher` para o Kestrel na sprint passada, teremos uma nova abstração chamada e acionada em substituição ao antigo motor Siemens.
- Criaremos uma interface base (caso já não exista) ex: `IOutputDriver` (ou reutilizá-la) que recebe a string path ou o modelo extraído.
- Substituiremos ou ramificaremos a lógica dentro de `MainWindow.xaml.cs` > `OnFileCreated` para, quando o protocolo ativo for `Rest Webhook`, acionar o despachante de envio web em vez de se conectar com a lib legada `ConnectML.Infrastructure.PlcDrivers`.

### C. Cliente HTTP e Injeção de Segurança
O fluxo disparará chamadas assíncronas de `HttpClient` e consumirá diretamente do Singleton de Configuração (`appsettings.json`) as chaves: `WebhookUrl`, `WebhookVerb` (POST, PUT), iterará na lista de `CustomHeaders` para aplicar os cabeçalhos registrados pelo Painel, e montará instâncias primitivas de Auth (Bearer e Basic) caso configurado. 

## 3. Lista de Tarefas por Componente (Execution Steps)

#### 1. Camada Infrastructure
- [ ] Instalar o pacote NuGet `Fluid.Core`.
- [ ] Criar interface e contrato `IWebhookOutboundDispatcher` sob a pasta `Core/Interfaces/`.
- [ ] Criar a implementação de envio REST na pasta `Infrastructure/Dispatchers/WebhookOutboundDispatcher.cs` injetando instâncias reutilizáveis de `HttpClient` com Timeout seguro.

#### 2. Engine Liquid / Payload Formatter
- [ ] Implementar a mecânica de parser Fluid que recebe o DTO base resultante da leitura do `.qif` (Contendo Status e Falhas), e processa sob a máscara da *String do Template* resgatada de `AppConfig.PayloadTemplate`.

#### 3. Integração com FileSystemWatcher (MainWindow.xaml.cs)
- [ ] Atualizar o Task Runner `OnFileCreated` do `FileSystemWatcher`. Criar ramificação semântica: `If (Perfil == Siemens)` faz lógica *TCP S7*; `Else if (Perfil == Webhook)` faz a conversão .QIF em JSON formatado e chama o `Dispatcher.SendAsync()`.
- [ ] Vincular cabeçalhos HTTP fixos do `CustomHeaders` populados pela UI na Sprint 2.

## 4. Critérios de Sucessos e Prevenção de Regressão Automática (Verification Plan)
A sprint é concluída demonstrando fluidez em um cenário isolado ponta a ponta sem derrubar as premissas atuais.
Como teste prático e visual:
1. Um arquivo simulado `.qif` de "Peça Aprovada" será drag-and-droplet (soltado) na pasta raiz lida pelo software WPF na aba "Arquivo de Entrada".
2. O Log visual identificará e renderizará o template Liquid mesclando para JSON.
3. Observar com interceptadores (Ex: Postman Mock Server / Webhook.site) o JSON injetado bater integralmente na URL de Destino do cliente sem bloqueios ou falhas de formatação.
