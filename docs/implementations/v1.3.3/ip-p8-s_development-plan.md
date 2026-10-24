# ConnectML - Plano de Execução da Release: Fase 8 (Versão 1.3.3)

## 🎯 Objetivo
Estruturar e executar com total segurança, rastreabilidade e integridade o ciclo completo de desenvolvimento, auditoria de segurança, documentação e deployment da **versão 1.3.3** do ConnectML, garantindo compatibilidade reversa absoluta e atualização transparente para clientes finais via Velopack e GitHub Releases.

---

## 🗺️ Mapa de Execução

| Etapa | Descrição |
| :--- | :--- |
| **Etapa 1** | **Auditoria de Resiliência e Segurança de Código**: Varredura de blocos `try/catch`, validação P/Invoke Win32 e eliminação de pontos de crash. |
| **Etapa 2** | **Blindagem de Migração de Configurações**: Validação do `EnsureSettingsMigrated()` para clientes v1.2.0 a v1.3.2 mantendo `%AppData%\ConnectML\appsettings.json` intocado. |
| **Etapa 3** | **Unificação Global de Versionamento (v1.3.3)**: Bump nos 4 projetos `.csproj`, título da `MainWindow.xaml`, rodapé e mutex de instância única em `Program.cs`. |
| **Etapa 4** | **Atualização de Documentações Técnicas e IA**: Atualização do `ARCHITECTURE.md`, `AI_CODEBASE_MAP.md` e criação dos relatórios de entrega. |
| **Etapa 5** | **Geração de Artefatos Visuais e Splashscreen**: Integração do banner oficial da v1.3.3 ao instalador Velopack (`--splashImage`). |
| **Etapa 6** | **Pipeline de Compilação Limpa e Empacotamento Velopack**: `dotnet publish` em modo Release autocontido + geração dos pacotes Delta e Full via `vpk pack`. |
| **Etapa 7** | **Publicação e Release no GitHub**: Criação de Tag Git `1.3.3`, publicação via `vpk upload github` / Release Notes e atualização dos links no `README.md`. |
| **Etapa 8** | **Merge e Fechamento de Histórico**: Merge seguro com a branch principal `main` mantendo rastreabilidade total. |
