# Relatório de Entrega: Sprint 6.6 - Release e Deployment v1.3.1

## 1. Visão Geral da Entrega
A **Sprint 6.6** encerrou com êxito o ciclo da **Fase 6 (Ponte Serial para Leitor de Código de Barras e Automação MeasurLink)** do ConnectML. Foi realizada a auditoria e unificação de versão em todos os projetos da solução para **1.3.1**, executada a revisão e merge da branch de desenvolvimento via Pull Request no GitHub, gerados os instaladores e pacotes diferenciais via Velopack e publicada oficialmente a versão 1.3.1 no GitHub Releases com suporte nativo a auto-update.

---

## 2. Detalhamento dos Sub-Sprints

### Sprint 6.6A: Auditoria e Ajustes de Versão Global (v1.3.1)
- **`ConnectML.Core.csproj`**: Confirmado em `<Version>1.3.1</Version>`.
- **`ConnectML.Infrastructure.csproj`**: Confirmado em `<Version>1.3.1</Version>`.
- **`ConnectML.Simulator.csproj`**: Confirmado em `<Version>1.3.1</Version>`.
- **`ConnectML.UI.csproj`**: Confirmado em `<Version>1.3.1</Version>`.
- **`MainWindow.xaml` / `Program.cs`**: Validados com título `ConnectML - V1.3.1`, rodapé `Versão 1.3.1` e busca por mutex de janela única.
- **`appsettings.json`**: Alinhado catálogo padrão com o comando `Desfazer (Alt + F + O)` e sequência de teclas `%{f}{o}`.
- **Compilação**: `dotnet build ConnectML.sln -c Release` aprovada com 0 erros e 0 avisos.
- **Commit**: Registrado em `feature/barcode-serial-monitor` (`01b9521`).

### Sprint 6.6B: Revisão da Branch e Merge com a `main`
- **Branch Remota**: Push da branch `feature/barcode-serial-monitor` para o GitHub.
- **Pull Request #6**: Criado via GitHub REST API detalhando o escopo integral da Fase 6:
  - [PR #6: feat(barcode): Ponte Serial para Leitor de Código de Barras e Automação MeasurLink (v1.3.1)](https://github.com/ramso-adnarim/ConnectML/pull/6)
- **Merge**: Aprovado e mesclado na branch `main` (commit merge `528c7e5`).
- **Sincronização**: Branch local `main` atualizada em fast-forward.

### Sprint 6.6C: Compilação Limpa e Empacotamento Velopack (v1.3.1)
- **Publicação**: `dotnet publish ConnectML.UI\ConnectML.UI.csproj -c Release --self-contained -r win-x64 -o .\publish`.
- **Velopack CLI**: `vpk pack --packId ConnectML --packAuthors "Protequality" --packTitle "ConnectML" --packVersion 1.3.1 --packDir .\publish --mainExe ConnectML.UI.exe --icon "ConnectML-logo-ico.ico" --shortcuts Desktop,StartMenu,Startup`.
- **Artefatos Gerados**:
  - `ConnectML-1.3.1-delta.nupkg` (311 KB — atualização diferencial super rápida)
  - `ConnectML-1.3.1-full.nupkg` (86.6 MB)
  - `ConnectML-win-Setup.exe` (91.3 MB)
  - `ConnectML-win-Portable.zip` (86.6 MB)
  - `RELEASES` e manifestos JSON atualizados.

### Sprint 6.6D: Publicação de Release no GitHub com Auto-Update
- **Tag e Release**: Tag `1.3.1` criada e enviada para o repositório remoto.
- **Upload Velopack**: `vpk upload github` executado com êxito, publicando todos os 6 ativos.
- **Release Notes**: Publicada com sumário técnico e operacional da versão 1.3.1.
- **Link Oficial**: [GitHub Release 1.3.1](https://github.com/ramso-adnarim/ConnectML/releases/tag/1.3.1).

### Sprint 6.6E: Atualização das Documentações Técnicas (IA-Ready)
- **`docs/ARCHITECTURE.md`**: Atualizado com detalhes do subsistema do leitor de código de barras, algoritmo de pontuação de processos em `WindowFocusHelper` (evitando falsos positivos de pastas do Windows Explorer), injeção de hardware scan codes via `MapVirtualKey`, cadência mnemônica com tecla Alt, recarregamento dinâmico via JSON e botão de acesso rápido às configurações.
- **`docs/AI_CODEBASE_MAP.md`**: Atualizado com glossário expandido e 12 regras de ouro para modelos de IA, prevenindo quebras de concorrência serial, foco indevido de janela e perda de configurações do usuário.
- **`docs/DEPLOYMENT_VELOPACK.md`**: Atualizado com comandos e fluxos oficiais da v1.3.1 e métricas do pacote delta.

### Sprint 6.6F: Atualização do README.md
- **`README.md`**: Atualizado com badges da v1.3.1, links diretos para download do instalador `ConnectML-win-Setup.exe` e da versão portátil `ConnectML-win-Portable.zip`, destaques da automação com o leitor serial e link para as release notes no GitHub.
