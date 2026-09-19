# Deploy Cheat Sheet (Velopack) - Versão 1.3.0

Este guia descreve os passos necessários para compilar, empacotar e distribuir o **ConnectML** utilizando o Velopack, cobrindo tanto distribuição local (Offline/LAN) quanto publicação remota com auto-update no GitHub Releases.

---

## 🚀 Pipeline de Build e Empacotamento

Abra o terminal PowerShell na **raiz do repositório** e execute os passos a seguir:

### 1. Limpeza e Publicação Autocontida
Garante que a compilação seja limpa e contenha todas as dependências nativas para Windows x64:

```powershell
# Remove a pasta de publish anterior para garantir uma build limpa
if (Test-Path .\publish) { Remove-Item -Recurse -Force .\publish }

# Publica a aplicação principal em modo Release autocontido para Windows 64-bits
dotnet publish ConnectML.UI\ConnectML.UI.csproj -c Release --self-contained -r win-x64 -o .\publish
```

### 2. Criar Pacotes de Instalação e Delta (Velopack)
Gera o instalador autônomo, arquivos portáteis, manifestos de release e pacotes diferenciais delta em `.\Releases`:

```powershell
vpk pack --packId ConnectML --packAuthors "Protequality" --packTitle "ConnectML" --packVersion 1.3.0 --packDir .\publish --mainExe ConnectML.UI.exe --icon "ConnectML-logo-ico.ico" --shortcuts Desktop,StartMenu,Startup
```

> **Dica sobre Pacotes Delta**: Ao manter versões anteriores dentro da pasta `.\Releases`, o `vpk pack` automaticamente calcula as diferenças binárias e gera um pacote diferencial (ex: `ConnectML-1.3.0-delta.nupkg` de ~288 KB), otimizando radicalmente a velocidade de atualização dos usuários.

### 3. Publicar Release no GitHub com Auto-Update
Para disponibilizar a nova versão para download e acionar a notificação automática nos clientes instalados:

```powershell
# Carregue o token de autenticação com permissão de escrita no repositório
$token = $env:GITHUB_TOKEN

# Envia os pacotes gerados em .\Releases para o GitHub Releases
vpk upload github --outputDir Releases --repoUrl "https://github.com/ramso-adnarim/ConnectML" --token $token --tag "1.3.0" --releaseName "1.3.0" --publish
```

---

## 📦 Estrutura dos Artefatos em `.\Releases`

| Arquivo | Descrição |
| :--- | :--- |
| **`ConnectML-win-Setup.exe`** | Instalador executável moderno com suporte a atalhos no Desktop, Menu Iniciar e Startup. |
| **`ConnectML-win-Portable.zip`** | Versão compactada para execução direta sem necessidade de instalação. |
| **`ConnectML-1.3.0-full.nupkg`** | Pacote completo com todos os binários da versão para novas instalações. |
| **`ConnectML-1.3.0-delta.nupkg`** | Pacote diferencial leve contendo apenas as alterações em relação à versão anterior. |
| **`RELEASES` & `releases.win.json`** | Manifestos com hashes SHA1/SHA256 lidos pelo `VelopackApp` para verificar atualizações. |
