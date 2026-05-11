# **Plano de Desenvolvimento: ConnectML Fase 2 \- Evolução Bidirecional e Webhooks Genéricos**

Este documento consolida as diretrizes arquiteturais, fluxos de dados e requisitos de interface para a Fase 2 do ConnectML. O objetivo central é evoluir o sistema de uma integração unidirecional (fixa para PLCs Siemens S7) para um middleware bidirecional baseado em eventos, implementando um "Canal REST JSON Genérico" configurável, eliminando integrações hardcoded (como a da EGA).

## **1\. Visão Arquitetural e Hospedagem (Hosting)**

Para suportar requisições externas sem perder a interface gráfica existente, a arquitetura fundirá os mundos Desktop e Web.

* **.NET Generic Host no WPF:** A aplicação WPF utilizará o .NET Generic Host para embutir o servidor web Kestrel. Isso é ativado pela adição da referência \<FrameworkReference Include="Microsoft.AspNetCore.App" /\> no projeto.  
* **Inicialização Assíncrona:** Para respeitar o modelo STA (Single-Threaded Apartment) do WPF, o Kestrel será iniciado de forma assíncrona não-bloqueante, rodando em background. O ciclo de vida do servidor será amarrado ao ciclo de vida da janela principal via IHostApplicationLifetime.  
* **Injeção de Dependência Compartilhada:** O IServiceCollection unificará os singletons do ConnectML.Core, permitindo que a interface gráfica (ViewModels) e os endpoints da API compartilhem o mesmo estado em memória e regras de negócio.  
* **Padrão Strategy com Keyed Services:** A injeção de dependência utilizará os *Keyed Services* do .NET 8 para instanciar a estratégia de comunicação correta (ex: Siemens S7 ou Webhook Genérico) no momento da execução, garantindo total desacoplamento e escalabilidade.

## **2\. Fluxo Bidirecional de Dados**

A arquitetura passa a suportar fluxos de entrada (Inbound) e saída (Outbound) de forma simultânea e independente.

### **2.1 Fluxo Inbound: Recebendo Comandos (API \-\> ConnectML \-\> MeasurLink)**

A aplicação escutará comandos externos para iniciar rotinas remotamente.

* **Endpoint Minimal API:** O Kestrel hospedará rotas estruturadas usando a abordagem de Vertical Slices (ex: /api/trigger/go).  
* **Validação de Payload:** O ConnectML receberá um JSON genérico contendo a ação (ex: iniciar inspeção), Nome da Rotina e Corrida.  
* **Processamento no Core:** O comando será desserializado, validado e roteado pelo barramento interno.  
* **Repasse Serial:** Os dados otimizados serão escritos em uma Porta Serial Virtual (COMx) para interagir com o software de metrologia MeasurLink.  
* **Sincronização de UI:** Qualquer atualização visual (como logs na tela) proveniente destas threads de background utilizará o Dispatcher para retornar à thread principal da UI com segurança.

### **2.2 Fluxo Outbound: Enviando Resultados (MeasurLink \-\> ConnectML \-\> API Externa)**

O sistema enviará ativamente os resultados processados para plataformas de terceiros.

* **Monitoramento e Parsing:** O FileSystemWatcher detecta novos arquivos .qif do MeasurLink, que são extraídos pelo QifParser.  
* **Payload Shaping com Liquid:** Os dados extraídos (Status, Rotina, Evento Estatístico) não terão um JSON fixo no código. Eles passarão por um motor de templates Liquid (biblioteca Fluid), onde serão formatados conforme o "molde" configurado pelo customer.  
* **Webhook Dispatcher:** Um cliente HTTP interno realizará um POST assíncrono do payload resultante para a URL de destino configurada.

## **3\. Segurança e Resiliência Industrial**

Ambientes de chão de fábrica exigem robustez extrema contra falhas de rede e segurança contra injeção de comandos.

* **Autenticação e Validação HMAC:** Requisições não serão abertas. Será implementada validação via assinatura HMAC SHA-256. O ConnectML gerará um hash com uma Chave Secreta (Secret Token) configurada localmente e o comparará com o cabeçalho recebido (ex: x-ega-signature ou X-Hub-Signature-256). O mesmo processo de assinatura será feito no fluxo de saída.  
* **Mitigação de Replay Attacks:** Inclusão de verificação de Timestamps nos payloads (ex: event\_ts) para garantir a ordenação e rejeitar requisições muito antigas ou duplicadas.  
* **Resiliência com Polly:** O fluxo de saída utilizará a biblioteca Polly para implementar *Exponential Backoff* (tentativas em 5 min, 20 min, 60 min) em casos de erro 5xx ou Timeouts, garantindo que o sistema de chão de fábrica não perca dados por instabilidades na nuvem. Erros graves ou limite de tentativas esgotado encaminharão o pacote para uma Dead-Letter Queue para avaliação manual.

## **4\. UI e Parametrização Genérica**

A interface gráfica WPF será remodelada para suportar qualquer customer (abandonando o termo fixo de um sistema específico). Os dados serão persistidos no appsettings.json.

* **Painel de Hospedagem (Inbound):** Toggles para ativar o servidor Kestrel local, definindo a Porta TCP de escuta e a Porta Serial de repasse para o MeasurLink.  
* **Painel de Conexão (Outbound):** Campos para URL do Endpoint (Webhook URL) e Verbo HTTP (POST, PUT, PATCH).  
* **Configurações de Segurança:** Menu dinâmico para seleção do tipo de autenticação (None, Basic Auth, Token/API Key, ou HMAC com campo para a Secret).  
* **Custom Headers:** Tabela iterativa para o usuário cadastrar chaves e valores personalizados no cabeçalho (ex: Content-Type, tokens customizados).  
* **Editor de Template JSON:** Uma caixa de texto para o usuário colar o template Liquid ({{ variavel }}), definindo a exata estrutura JSON aguardada pelo sistema receptor.  
* **Políticas de Falha:** Configurações numéricas para Timeout de conexão e Limite máximo de tentativas de reenvio.

## **5\. Roteiro de Implementação e Próximos Passos**

*Consulte o arquivo docs/PHASE2\_EXECUTION\_STEPS.md para o detalhamento técnico de execução estruturada por fases.*