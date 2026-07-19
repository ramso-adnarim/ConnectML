# VISÃO GERAL ARQUITETURAL - FASE 4 (V1.1.0)

A Fase 4 do ConnectML eleva a maturidade do produto, focando em resiliência, experiência do usuário (UX) avançada e rastreabilidade profunda. Esta versão (V1.1.0) introduz mecanismos de recuperação autônoma e interface dinâmica para atender ambientes corporativos complexos do *customer*.

## 1. Single Instance (Mutex) e Versionamento
Para garantir a integridade dos dados e evitar conflitos de portas de comunicação (seja REST ou S7), o sistema garantirá a execução de uma única instância.
- **Mecanismo:** Utilização da classe `System.Threading.Mutex` no ponto de entrada da aplicação (`Program.cs`).
- **Comportamento:** Ao detectar uma instância já em execução (mesmo minimizada no System Tray), a nova tentativa de abertura será bloqueada e o processo secundário será encerrado. A instância primária deverá, idealmente, ser notificada para ser trazida ao primeiro plano.

## 2. Motor de Retry Progressivo e Resiliência
A aplicação frequentemente opera em regime de Auto-Start em máquinas de chão de fábrica. É comum a aplicação iniciar antes da rede ou do CLP (PLC) estarem operacionais.
- **Janela de Alerta Não-Bloqueante:** Substituição de `MessageBox` estáticas por uma janela customizada efêmera. Se a conexão inicial falhar, essa janela exibirá um *countdown* de 6 segundos e fechará automaticamente.
- **Loop de Retry:** Após o fechamento da janela de alerta, a interface principal é trazida ao primeiro plano (se minimizada) e inicia-se um loop assíncrono de reconexão a cada 5 segundos.
- **Logging e Silenciamento de Erros:** Durante o loop de *retry*, exceções de conexão não geram novos pop-ups. Todo o rastreamento é feito via Serilog e refletido no painel de Logs.
- **Abort:** O *customer* pode intervir clicando no botão "Parar", que cancela o fluxo (*CancellationToken*) da rotina de *retry* imediatamente.
- **Recuperação:** Ao obter sucesso na conexão, o loop é encerrado, o monitoramento normal é iniciado e a interface é minimizada novamente para o System Tray.

## 3. Escrita de Strings (Part Number) via Siemens S7
O protocolo S7 trata Strings de forma peculiar. Diferente de booleanos ou numéricos (*words*), uma String no S7 possui um cabeçalho (*header*) de 2 bytes que precisa ser estritamente respeitado.
- **Byte 0:** Tamanho máximo da String definido na DB.
- **Byte 1:** Tamanho real (atual) da String contida.
- **Bytes subsequentes:** Caracteres ASCII/UTF-8.
A integração utilizará a biblioteca `S7NetPlus` para escrever adequadamente esses bytes, garantindo que o CLP do *customer* reconheça a mudança no comprimento real ao receber o `productname` (Part Number) originado do QIF.

## 4. UI Dinâmica e Responsiva
A seção de Configuração abandona seleções estáticas em favor de uma lista dinâmica (`ItemsControl`), permitindo ao *customer* construir o payload ou mapeamento de forma flexível.
- Limite de 1 a 3 campos de configuração.
- Validação in-line não intrusiva (sem `MessageBox`) para o protocolo Webhook REST, garantindo que o template inserido possui as tags (ex: `{{ PartNumber }}`) correspondentes aos campos adicionados. Bloqueia-se o início caso a validação falhe.
- **Painel S7 Responsivo:** As caixas de configuração de DB (Booleana, Inteira, PartNumber) só estarão visíveis se o respectivo tipo tiver sido adicionado na configuração geral. O painel ajustará sua altura automaticamente.
- **Teste de String:** Botão transiente ao lado da configuração S7 de PartNumber para injetar valores de teste (ex: "True" / "False" em formato de texto) facilitando o comissionamento em campo.

## 5. Interações Avançadas de UX
- **Notificações de Balão (System Tray):** Uso do `ShowBalloonTip` para informar o *customer* sobre lotes processados e erros críticos enquanto a janela estiver minimizada, fornecendo feedback de rastreabilidade (Resultados e Part Number) sem poluir a tela.
- **Menu Hamburger (Colapsável):** Foco operacional. Um botão superior permitirá recolher todo o painel de configurações (lado esquerdo), expandindo a visualização de Logs para ocupar 100% da largura da aplicação, ideal para diagnósticos em tempo real da linha de produção.
