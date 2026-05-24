# ConnectML - Phase 3 Execution Plan (Sprints)

Este plano detalha a execução sequencial da Fase 3, garantindo entregas incrementais sem quebrar a estabilidade atual.

## Sprint 1: O Novo ConnectML.Simulator (Servidor S7)
**Objetivo:** Substituir o simulador de TCP Listener básico por um servidor PLC virtual funcional e interativo.
*   **Tarefas:**
    *   [x] Integrar `Snap7` (modo Server) ou `Sharp7` no projeto `ConnectML.Simulator`.
    *   [x] Implementar bancos de memória (DBs) virtuais atendendo na porta 102.
    *   [x] Implementar rotina de decodificação no simulador para logar requisições recebidas (ex: decodificar S7 Header).
    *   [x] Desenvolver uma CLI local no simulador capaz de aceitar comandos de leitura (`DBX.Y=?`) e escrita (`DBX.Y=Z`).
*   **Validação:** Executar o simulador, escrever via ConnectML e confirmar a decodificação; interagir via CLI e verificar os valores em memória.

## Sprint 2: UI e Configurações para Handshake (DB Status)
**Objetivo:** Adicionar o novo campo de configuração do "DB (Status)" e prepará-lo no modelo de dados.
*   **Tarefas:**
    *   [x] Atualizar `MainWindow.xaml` para incluir o *Label* e *TextBox* "DB (Status)".
    *   [x] Atualizar a classe `AppConfig` (e `appsettings.json`) para suportar a nova variável `DbStatusAddress`.
    *   [x] Garantir que o valor preenchido seja carregado e salvo corretamente ao abrir/fechar a aplicação.
*   **Validação:** Abrir aplicação, digitar endereço de DB (ex: `DB10.DBX0.2`), fechar e reabrir validando a persistência do valor.

## Sprint 3: Lógica de Handshake S7
**Objetivo:** Integrar a escrita do `Status` na máquina de estados principal quando processando QIF.
*   **Tarefas:**
    *   [x] Atualizar o método responsável pelo envio para o PLC (`SiemensS7Client` ou dispatcher equivalente).
    *   [x] Após escrever a variável de resultado (Booleana/Inteira), adicionar a instrução de escrita `1` no endereço configurado de `DB (Status)`.
    *   [x] Adicionar logs detalhados na UI descrevendo cada passo ("Escrevendo Resultado...", "Escrevendo Handshake Status 1").
*   **Validação:** Executar o ciclo completo com o novo Simulador (Sprint 1) rodando e validar se ambas as variáveis foram escritas sequencialmente com sucesso. O Simulador pode então via CLI ser usado para resetar o status, simulando o PLC do *customer*.

## Sprint 4: Painel de Teste Transiente de Comunicação
**Objetivo:** Adicionar as ferramentas de diagnóstico integradas na UI para comissionamento rápido.
*   **Tarefas:**
    *   [x] Desenhar ícones SVG minimalistas para "Toggle", "Ler" e "Incrementar".
    *   [x] Adicionar estes botões adjacentes aos campos "DB Booleana", "DB Inteira" e "DB Status" no `MainWindow.xaml`.
    *   [x] Implementar a lógica de *binding*: estes botões devem estar invisíveis ou desabilitados quando o status do serviço for "Rodando".
    *   [x] Criar um método transiente que abra uma conexão S7 (independente), leia/escreva o dado, feche a conexão e logue o resultado na tela.
    *   [x] Mapear os eventos de clique dos botões para essa nova infraestrutura transiente.
*   **Validação:** Com o ConnectML "Parado" e Simulador rodando, clicar em "Toggle" da Booleana e "Ler" do Status, verificando os resultados na UI e no console do Simulador, assegurando que o *socket* foi encerrado logo em seguida.

## Sprint 5: Auto-Start e Minimização Autônoma (Resiliência)
**Objetivo:** Garantir a volta automática do ConnectML em caso de reinício do computador do *customer*.
*   **Tarefas:**
    *   [x] Adicionar a `CheckBox` "Reinício Automático" próximo ao botão Iniciar na UI.
    *   [x] Adicionar `AutoStartEnabled` and `LastRunSuccessful` no `AppConfig` (`appsettings.json`).
    *   [x] No fluxo de sucesso ao "Iniciar" o serviço, setar `LastRunSuccessful = true`. Se ocorrer falha de conexão na partida, setar para `false`.
    *   [x] No evento `Window_Loaded` (ou fim da inicialização WPF): Avaliar `AutoStartEnabled && LastRunSuccessful`. Se verdadeiro, invocar evento de clique do botão "Iniciar" e disparar lógica de *System Tray* para ocultar a janela.
*   **Validação:** Habilitar checkbox, iniciar com sucesso. Forçar fechamento da aplicação. Reabrir a aplicação e constatar que ela inicia o serviço e esconde a janela sozinha automaticamente sem intervenção manual.
