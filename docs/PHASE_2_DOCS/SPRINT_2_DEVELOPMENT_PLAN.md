# ConnectML - Plano de Desenvolvimento: Fase 2 / Sprint 2
**Etapa 2: Parametrização e Interface Gráfica (UI Genérica)**

## Objetivos e Escopo
Esta etapa objetiva adaptar a interface (UI) gráfica do MainWindow.xaml para a nova realidade agnóstica de mensageria JSON, removendo os _hardcodes_ de comunicação fixa que tínhamos (API EGA) e adotando o conceito universal de **Webhooks REST**. Toda modificação efetuada deve ser refletida via leitura/escrita no appsettings contínuo do projeto.

## Tarefas de Desenvolvimento

### 1. Refatoração Visual da Aba de Integração
- **Arquivo Alvo:** `ConnectML.UI/MainWindow.xaml` e `MainWindow.xaml.cs`
- **Ação:** Permitir a seleção entre "Siemens S7" ou "Webhook REST Genérico". Para a última opção, substituir interfaces antigas por painéis dinâmicos compreendendo as propriedades do endpoint externo a qual postaremos os resultados.

### 2. Componentização de Propriedades do Webhook (Outbound)
Os antigos dados específicos serão limpos para ceder lugar a:
  - **Endpoint e Verbos:** Componentes para input da Payload URL (*TextBox*) e HTTP Method (*ComboBox* focado em POST/PUT/PATCH).
  - **Autenticação:** Dropdown que habilitará visualmente um campo suplementar de `Secret/Token` se a opção não for "None".
  - **Estruturação Padrão do JSON (Template Engine):** Adição de um `<TextBox>` com ampla varredura multilinhas para aceitar `{{ Variáveis de Interpolacao }}` formatados pelos clientes antes do despacho.

### 3. Gerenciamento e Persistência de Modelos
- **Arquivos Alvo:** `ConnectML.UI/Models/AppConfig.cs`
- **Ação:** Criação das propriedades aderentes a configuração: `WebhookUrl`, `WebhookVerb`, `AuthType`, `AuthToken` e `PayloadTemplate`. O método `SaveSettings` garantirá que, ao reiniciar a janela, a memória da UI refaça integralmente o layout desejado com as preferências salvas no objeto JSON local do disco.

## Verificação e Critérios de Sucesso
- A UI interativa transita fluidamente entre mostrar as opções Siemens e as opções do novo Webhook Genérico.
- Sem *crashes* ao lidar com serialização de propriedades extensas do Template Liquid nos eventos de _Load_.
- A formatação de cores e bordas mantém a responsividade padrão da aplicação.
