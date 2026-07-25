# Implementation Plan: Sprint 4 - Velopack e Atualização via GitHub (Versão 1.2.0)

Este plano detalha a transição do ConnectML para a versão `1.2.0` e a implementação completa de atualizações automáticas via Velopack apontando para o repositório do GitHub.

> [!IMPORTANT]
> **User Review Required**
> As alterações contidas neste plano envolvem publicação direta e simulação de releases no GitHub para fins de teste. Valide as etapas abaixo, especialmente o processo duplo de empacotamento (v1.2.0 -> v1.2.1), antes de iniciarmos a alteração do código.

## 1. Alteração de Versão Global (Para 1.2.0)
Vamos atualizar todos os pontos focais da aplicação que travam o número da versão:
- **[MODIFY]** `ConnectML.UI.csproj`: Alterar a tag `<Version>` para `1.2.0`.
- **[MODIFY]** `Program.cs`: Atualizar a busca da janela do Mutex de `1.1.0` para `"ConnectML - V1.2.0"`.
- **[MODIFY]** `MainWindow.xaml`: Atualizar a propriedade `Title` da janela principal para `"ConnectML - V1.2.0"`.

## 2. Implementação da UI e Funcionalidade (Velopack)
- **[MODIFY]** `MainWindow.xaml`: O `TextBlock` isolado no canto inferior direito ("Versão 1.1.0") será envelopado em um componente interativo (Cursor="Hand").
- **Visual Feedback**: Adicionaremos um ícone ao lado do texto da versão:
  - ✔️ (*Check* - cor neutra) se o aplicativo já for o mais atual.
  - ⚠️ (*Warning* - cor de alerta) se existir uma nova versão nas Releases do GitHub.
- **[MODIFY]** `MainWindow.xaml.cs` (ou via orquestração na ViewModel): Implementar rotinas do `UpdateManager` (Velopack) apontando para o seu repositório. O processo será:
  1. No _Startup_, a aplicação checa silenciosamente por atualizações. Se encontrar, altera o ícone para ⚠️.
  2. Quando o usuário clica sobre a área da versão com o aviso, a aplicação aciona o download e chama `ApplyUpdatesAndRestart()`.

## 3. Ciclo de Deploy e Teste Validatório (Velopack CLI)
Para atestar o sistema sem deixar margem a dúvidas, faremos o seguinte fluxo iterativo no console:

1. **A Base (v1.2.0)**:
   - Atualizaremos o código para a versão `1.2.0`.
   - Executaremos `dotnet publish` + `vpk pack`.
   - Executaremos `vpk upload github` para injetar a Release `v1.2.0` diretamente no seu repositório remoto.
   - **Ação humana necessária**: Você rodará o executável *Setup* na sua máquina e instalará essa versão.

2. **A Nova Release (v1.2.1 - Para Teste de Atualização)**:
   - Imediatamente após isso, simularemos que criamos uma feature nova bumpando a versão da UI para `v1.2.1`.
   - Executaremos todo o pipeline de pacote de novo, e daremos um `vpk upload github` publicando a `v1.2.1`.

3. **O Momento da Verdade**:
   - Você então abrirá o seu ConnectML v1.2.0 instalado localmente.
   - A interface vai pingar o GitHub, descobrir a `v1.2.1` e acender o ícone de Warning ⚠️ no canto.
   - Você clicará, e a aplicação fechará e reabrirá instantaneamente já na nova versão, atestando o fluxo completo.

## Open Questions

- Como deseja a usabilidade do download? O ConnectML já deve baixar os novos pacotes (`.nupkg` delta) em background silenciosamente logo ao abrir e mostrar o Warning apenas pedindo "clique aqui para reiniciar", ou você prefere que o download em si só aconteça quando o usuário decidir clicar no Warning?
- Sobre os ícones Check/Warning: podemos utilizar Path geometries (vetores nativos desenhados em XAML) para não dependermos de fontes externas ou imagens importadas. De acordo?
