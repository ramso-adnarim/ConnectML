# ConnectML - Phase 3 Architectural Overview

## Objetivo
A Fase 3 foca na preparação do motor Siemens S7 Profinet para *deployment* real em um ambiente de *customer*. O foco principal é trazer robustez, criar um *handshake* de comunicação seguro, adicionar ferramentas de diagnóstico nativas na interface do usuário (UI) e evoluir nosso Simulador para um comportamento fiel ao hardware real.

## Pilares da Fase 3

### 1. Evolução do `ConnectML.Simulator` (Servidor S7 Virtual)
O atual `ConnectML.Simulator`, que funcionava como um *TCP Listener* genérico, será substituído por um servidor S7 real.
*   **Tecnologia:** Utilização do `Snap7` (modo Server) ou `Sharp7` para simular um PLC (porta 102).
*   **Mapeamento de Memória:** Implementação de bancos de memória (DBs) para responder corretamente aos comandos de leitura e escrita.
*   **Visualização e Logs:** Decodificação das requisições para exibir mensagens legíveis no console (ex: `"ConnectML escreveu TRUE na DB10.0"`).
*   **CLI (Console Interativo):** Capacidade do desenvolvedor/testador de interagir com as DBs simuladas em tempo real via terminal do simulador.
    *   *Escrita:* `DB10.1=1` (seta o bit) ou `DB10.1=0`
    *   *Leitura:* `DB10.0=?` (retorna o valor atual do bit/word)

### 2. Handshake de Sincronização (Controle via DB Status)
Para garantir a consistência dos dados, o PLC do *customer* precisa de uma confirmação exata de que o ConnectML terminou o ciclo de processamento do arquivo `.qif`.
*   **Adição na UI:** Novo campo de configuração "DB (Status)" abaixo das DBs existentes.
*   **Fluxo de Handshake:**
    1.  O PLC inicia o ciclo mantendo a `DB (Status)` em `0`.
    2.  ConnectML detecta e lê o arquivo `.qif`.
    3.  ConnectML escreve o resultado (OK/NG) na DB Booleana ou Inteira (conforme configurado).
    4.  ConnectML escreve `1` na `DB (Status)` confirmando o fim da operação.
    5.  O PLC consome os dados de resultado e reseta a `DB (Status)` para `0`, liberando um novo ciclo.
*   **Logs:** Todo esse ciclo deve ser explicitamente registrado na `RichTextBox` de logs da UI do ConnectML.

### 3. Painel de Teste de Comunicação (Ferramentas de Diagnóstico na UI)
Para facilitar o comissionamento diretamente na máquina do *customer*, ferramentas de teste pontual serão integradas à UI. Estas ferramentas garantirão a saúde da conexão antes da inicialização do serviço principal.
*   **Regra de Exibição:** Estes botões só estarão visíveis e ativos quando o serviço estiver **PARADO**.
*   **Botões Inline com Ícones SVG:**
    *   *DB Booleana:* Botão de "Toggle" que escreve `1` ou `0` na respectiva DB.
    *   *DB Inteira:* Botão de "Incremento" (soma +1 ao valor atual e escreve, resetando ao chegar em 5) para validar a escrita de *words*.
    *   *DB Status:* Dois botões independentes: um de "Ler" (busca o valor no PLC e exibe nos logs) e um de "Escrever Toggle" (0/1).
*   **Ciclo de Vida Transiente:** Cada clique instancia uma **nova conexão**, executa a ação, loga o resultado e a fecha imediatamente. Isso evita conflitos e vazamento de *sockets* quando o serviço principal ("Iniciar") for acionado (que terá sua conexão dedicada).

### 4. Inicialização Autônoma (Resiliência)
Se o computador do *customer* reiniciar devido a uma queda de energia ou atualização, o ConnectML deve se recuperar sozinho sem intervenção manual.
*   **Nova Configuração UI:** Um *checkbox* discreto "Reinício Automático" próximo ao botão "Iniciar".
*   **Persistência:** O estado do *checkbox* será salvo no `appsettings.json`, assim como um marcador interno do estado de "sucesso da última execução".
*   **Lógica de Recuperação (`Window_Loaded`):** Se o *checkbox* estiver habilitado e a última execução anterior registrou sucesso na conexão com o PLC:
    1.  O evento de clique do botão "Iniciar" é invocado automaticamente.
    2.  A janela é imediatamente minimizada para a *System Tray* (reaproveitando a lógica implementada na Fase 2), rodando de forma invisível para o operador.
