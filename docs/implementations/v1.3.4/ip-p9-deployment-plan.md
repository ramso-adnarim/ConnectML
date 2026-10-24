# ConnectML - Plano de Execução da Release: Fase 9 (Versão 1.3.4)

## 🎯 Objetivo
Estruturar e executar com total segurança, rastreabilidade e integridade o ciclo completo de auditoria de código, blindagem de exceções, preservação de configurações no Velopack, geração de artefatos visuais, empacotamento e deployment da **versão 1.3.4** do ConnectML no GitHub Releases.

---

## 🗺️ Mapa de Execução do Deployment

| Etapa | Descrição | Status |
| :--- | :--- | :---: |
| **Etapa 1** | **Auditoria de Resiliência e Segurança de Código**: Varredura de blocos de interface (WPF Dispatcher, HUD, Start/Stop/ProcessFile) com adição de blindagem try/catch contra crash. | Concluída |
| **Etapa 2** | **Blindagem de Migração de Configurações**: Validação do `EnsureSettingsMigrated()` para clientes de versões anteriores mantendo `%AppData%\ConnectML\appsettings.json` 100% preservado. | Concluída |
| **Etapa 3** | **Unificação Global de Versionamento (v1.3.4)**: Bump de versão consolidado nos 4 `.csproj`, título, rodapé e Mutex em `Program.cs`. | Concluída |
| **Etapa 4** | **Atualização de Documentações Técnicas e IA**: Sincronização de `docs/ARCHITECTURE.md`, `docs/AI_CODEBASE_MAP.md` (regras 3.15 a 3.18) e `docs/DEPLOYMENT_VELOPACK.md`. | Concluída |
| **Etapa 5** | **Atualização do README.md**: Apresentação elegante das novas funcionalidades da v1.3.4, preservação do histórico de versões e links de release. | Concluída |
| **Etapa 6** | **Geração de Artefatos Visuais e Splashscreen**: Criação e incorporação do novo banner oficial com identidade `V1.3.4` para o instalador Velopack (`splash.jpg`). | Concluída |
| **Etapa 7** | **Pipeline de Publicação e Empacotamento Velopack**: `dotnet publish` em modo Release autocontido + geração dos pacotes Delta e Full via `vpk pack`. | A Executar |
| **Etapa 8** | **Publicação e Release no GitHub**: Publicação via `vpk upload github` / `gh release create` com tag `1.3.4` e Release Notes formatadas. | A Executar |
| **Etapa 9** | **Merge e Fechamento de Histórico**: Merge seguro com a branch principal `main` mantendo rastreabilidade total. | A Executar |

---

## 🛡️ Checklist de Segurança Pré-Deploy

- [x] Nenhuma alteração no envio de strings para o CLP Siemens S7 (`result.Product`) ou Webhook REST.
- [x] O método `QifParser.Parse` possui tratamento de exceção com fallback resiliente e retorno seguro.
- [x] Métodos de transição de estado do HUD protegidos contra `Dispatcher.HasShutdownStarted` e exceções de fechamento.
- [x] Diretório `%AppData%\ConnectML\appsettings.json` protegido contra sobrescrita por templates padrão.
- [x] Imagem de Splashscreen gerada em alta definição com a versão exata `V1.3.4`.
