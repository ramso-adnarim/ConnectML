# ConnectML - Plano de Desenvolvimento: Fase 2 / Sprint 1
**Etapa 1: A Fundação Híbrida (WPF + Kestrel)**

## Objetivos e Escopo
A primeira etapa ("Sprint 1") da Fase 2 visa preparar o terreno para as capacidades bidirecionais e webhooks REST. Para atingir isso, será necessário fundir o mundo de uma aplicação Desktop (WPF) com um servidor Web (.NET Generic Host e Kestrel) sem causar travamentos na interface de usuário (Thread UI / STA).

## Tarefas de Desenvolvimento

### 1. Inclusão do Framework Web no Projeto WPF
- **Arquivo Alvo:** `ConnectML.UI/ConnectML.UI.csproj`
- **Ação:** Será injetada a referência de framework `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Esta injeção garante que o projeto utilize as bibliotecas do ASP.NET Core sem perder sua natureza Desktop (`OutputType=WinExe`).

### 2. Criação do Entrypoint Personalizado (Program.cs / App.cs)
- **Cenário Atual:** A aplicação inicia através do código auto-gerado do arquivo `App.xaml`.
- **Ação:**
  - Configurar a inicialização do `.NET Generic Host` (`IHost`) diretamente na sobrescrita do método `OnStartup(StartupEventArgs e)` dentro de `App.xaml.cs`.
  - Configurar injeção de dependência (`IServiceCollection`) no host, que servirá tanto aos serviços Web (Minimal APIs) quanto às regras de negócio atuais da aplicação.

### 3. Configuração do Servidor Kestrel em Background
- **Configurações Prévias:** Garantir que o Kestrel rodará de modo assíncrono não-bloqueante (`await host.StartAsync()`), preferencialmente usando a porta especificada nas configurações padrão ou porta 5000.
- **Ciclo de Vida:** O serviço web será engatado ao método `OnExit` do `App.xaml.cs` (ou ao `IHostApplicationLifetime`), acionando um `await host.StopAsync()` limpo e gracioso do servidor quando a janela do WPF fechar, garantindo que não deixemos portas presas no sistema operacional.

## Verificação e Critérios de Sucesso
Após a execução destas atividades técnicas, a validação será feita da seguinte maneira:
1. Iniciando o ConnectML normalmente (com a UI abrindo e responsiva).
2. Verificando os logs internos e atestando que o Kestrel está escutando na porta configurada.
3. Evidenciando, através do Postman, a possibilidade de fazer requisições HTTP para a porta configurada no WPF.

*Esta fase será construída e versionada isoladamente para evitar instabilidades antes da integração pesada da UI na Sprint 2.*
