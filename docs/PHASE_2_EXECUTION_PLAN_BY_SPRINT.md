# **Plano de Desenvolvimento por Etapas \- ConnectML Fase 2**

Para garantir a estabilidade do sistema e facilitar o rollback caso algo dê errado, o desenvolvimento do "Canal REST Genérico" foi dividido em **5 Etapas Sequenciais**. Cada etapa deve ser codificada, testada e comitada antes de avançar para a próxima.

## **Etapa 1: A Fundação Híbrida (WPF \+ Kestrel)**

**Objetivo:** Permitir que a aplicação WPF tenha "superpoderes" Web sem quebrar a interface gráfica ou travar a Thread de UI.

* **Tarefas de Código:**  
  1. Alterar o ConnectML.UI.csproj para incluir \<FrameworkReference Include="Microsoft.AspNetCore.App" /\>.  
  2. Modificar a inicialização no Program.cs para criar um .NET Generic Host.  
  3. Configurar o Kestrel para subir em background (StartAsync) associado ao IHostApplicationLifetime da janela WPF.  
* **Critério de Sucesso:** A aplicação WPF abre normalmente e, simultaneamente, o console indica que o Kestrel está escutando na porta configurada (ex: localhost:5000).

## **Etapa 2: Parametrização e Interface Gráfica (UI Genérica)**

**Objetivo:** Adaptar o MainWindow.xaml para a nova realidade agnóstica (removendo hardcodes específicos e adotando o conceito de Webhooks) e persistir essas configurações.

* **Tarefas de Código:**  
  1. Refatorar a aba de "Configuração de Saída" para permitir a escolha entre "Siemens S7" ou "Webhook REST".  
  2. Criar painéis dinâmicos:  
     * **Webhook URL** e **Verbo HTTP**.  
     * **Autenticação** (Dropdown: None, Basic, Bearer, HMAC Secret).  
     * **Headers Customizados** (DataGrid simples Chave/Valor).  
     * **Template de Payload** (TextBox grande para o motor Liquid).  
  3. Atualizar o sistema de load/save no appsettings.json para suportar esses novos campos.  
* **Critério de Sucesso:** A UI reflete a nova arquitetura e salva os dados corretamente sem erros ao reiniciar a aplicação.

## **Etapa 3: Fluxo Inbound (Minimal APIs e Despacho)**

**Objetivo:** Receber o comando externo "GO" de forma segura e refleti-lo no ConnectML e MeasurLink.

* **Tarefas de Código:**  
  1. Criar a rota Minimal API POST /api/webhooks/incoming isolada em um arquivo (ex: WebhookEndpoints.cs).  
  2. Implementar a lógica para receber o JSON agnóstico e roteá-lo para a interface de porta serial virtual.  
  3. Integrar o Application.Current.Dispatcher.Invoke para exibir logs de recebimento na interface gráfica sem quebrar o STA.  
* **Critério de Sucesso:** Enviar um POST via Postman/Curl para o ConnectML e visualizar a mensagem de sucesso na interface (Logs) do WPF.

## **Etapa 4: Fluxo Outbound (Motor de Templates e HttpClient)**

**Objetivo:** Pegar os dados do MeasurLink (QIF), injetá-los no template escolhido pelo customer e enviá-los para a nuvem/rede.

* **Tarefas de Código:**  
  1. Instalar a biblioteca Fluid.Core (Motor Liquid).  
  2. Criar a classe WebhookDispatcher implementando uma interface genérica (ex: IOutputDriver).  
  3. Ao ler um QIF, renderizar o template configurado injetando as variáveis (ex: substitui {{Status}} por "PASS").  
  4. Executar o HttpClient.PostAsync para a URL configurada anexando os Custom Headers.  
* **Critério de Sucesso:** Ao soltar um arquivo .qif na pasta, o ConnectML monta um JSON personalizado e envia com sucesso para o *Webhook.site* (ou outro receptor de testes).

## **Etapa 5: Segurança e Resiliência de Nível Industrial**

**Objetivo:** Proteger a API contra injeções falsas (HMAC) e evitar perda de dados por queda de rede (Polly).

* **Tarefas de Código:**  
  1. **Inbound:** Criar um Middleware (ou Endpoint Filter) que valide o cabeçalho X-Hub-Signature fazendo o cálculo HMAC SHA-256 do corpo recebido usando a Secret do customer.  
  2. **Outbound:** Instalar a biblioteca Polly. Envolver a chamada do HttpClient com uma política de *Exponential Backoff* (ex: tentar 3 vezes com espaçamento de 2s, 4s, 8s em caso de erro 503 ou Timeout).  
* **Critério de Sucesso:** O sistema recusa requisições do Postman sem a assinatura correta (Retorna 401). O ConnectML tenta reenviar dados múltiplas vezes quando o servidor de destino está simuladamente fora do ar.