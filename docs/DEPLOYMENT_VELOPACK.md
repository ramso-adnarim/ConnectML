# Deploy Cheat Sheet (Velopack)

Este guia descreve os passos necessários para compilar e gerar o instalador final do **ConnectML** utilizando o Velopack, com foco em distribuição local (Offline/LAN) para o usuário.

## 🚀 Comandos de Release

Abra o terminal na **raiz do repositório** e execute:

### 1. Limpar e Publicar
```powershell
# Remove a pasta de publish anterior para garantir uma build limpa
if (Test-Path .\publish) { Remove-Item -Recurse -Force .\publish }

# Publica a aplicação principal em modo Release para Windows 64-bits
dotnet publish ConnectML.UI\ConnectML.UI.csproj -c Release --self-contained -r win-x64 -o .\publish
```


### 2. Criar Pacote (Velopack)
Gera o instalador e arquivos de update em `Releases`.

```powershell
vpk pack --packId ConnectML --packAuthors "Protequality" --packTitle "ConnectML" --packVersion 1.0.0 --packDir .\publish --mainExe ConnectML.UI.exe --icon "ConnectML-logo-ico.ico" --shortcuts Desktop,StartMenu,Startup
```
