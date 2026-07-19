# SPRINT 5: Segurança e Resiliência de Nível Industrial

Documento de Referência da **Etapa 5** do *Projeto ConnectML Fase 2*.

## 1. Visão Geral da Sprint
A finalização da refatoração para nuvem/Cloud exige a elevação dos padrões do ConnectML de "Aplicação Desktop" para "Servidor Industrial Resiliente". Ambientes de chão de fábrica sofrem flutuações de rede e estão sujeitos à rede OT/IT. O objetivo desta sprint é emoldurar o recém nascido Kestrel Inbound e HttpClient Outbound com uma robusta camada criptográfica e tolerável a intempéries da infraestrutura da fábrica.

## 2. Abordagem Técnica e Entregáveis

### 2.1 Implementação de Tolerância a Falhas (Outbound)
Quando uma peça é medida pelo *MeasurLink* e formatada em QIF, e em seguida transformada via *Liquid Engine*, ela não pode ser perdida apenas pelo switch de rede ter reiniciado pontualmente.
*   **Abordagem C#:** Integração da biblioteca **Polly** (Aprovada pela fundação .NET). Nós criaremos uma política temporal (WaitAndRetryAsync) configurada para **Exponential Backoff**. Se a EGA API demorar ou a porta for recusada (503), o aplicativo re-tenta agressivamente e depois descansa espaçadamente, reportando os logs diretamente na RichTextBox do Usuário para transparência de suporte.

### 2.2 Blindagem Anti-Intrusos e Confiança Zero (Inbound/Outbound)
Os Webhooks trazem uma superfície de perigo que portas seriais trancafiadas fisicamente (ComPorts) não traziam. Hackers na mesa da fábrica agora podem fazer um "Curl POST" para a porta principal da máquina se passarem pela LAN.
*   **Abordagem C#:** Implementar **HMAC-SHA256**. Assim como o ecossistema do GitHub Actions usa o `X-Hub-Signature-256`, o ConnectML lerá o payload recebido (stream UTF-8), misturará com a Secret (salva pelo cliente no Painel) e gerará um Hex Code hashável local. Se esse Hash não bater em 100% com o exposto na Requisição HTTP de chegada, faremos um Drop de pacote (Unauthorized 401) sem que a linha Siemens Serial e o MeasurLink desconfiem, mantendo as engrenagens imaculadas na linha de produção.

## 3. Fluxo de Entrega Programada
1. Baixar biblioteca `Polly`.
2. Refatorar o Singleton `WebhookOutboundDispatcher.cs` injetando Polly Retries em um try/catch envolto no pacote lógico. 
3. Engatar leitura criptográfica manual do X-Hub no método `POST /api/webhooks/incoming`.
4. Permitir injeção reversa do `HMAC` gerado pelo ConnectML no cabeçalho Outbound caso o painel Autenticação esteja no modo "HMAC (Secret)".
5. Consolidamento do Workflow e testes de integridade usando o Webhook.site e Curl.
