# AI Codebase Map: ConnectML

Este documento serve como um mapa e glossário de domínio projetado especificamente para agentes de IA que farão manutenção no **ConnectML**. O objetivo é prover contexto imediato sobre as regras de negócio essenciais, glossário e as armadilhas (gotchas) arquiteturais da base de código.

---

## 1. Glossário de Domínio

- **ConnectML**: Middleware que atua como ponte entre o software de metrologia (MeasurLink/Mitutoyo) e controladores lógicos programáveis (PLCs, primariamente Siemens S7).
- **QIF (Quality Information Framework)**: Formato de arquivo XML utilizado para exportar os resultados de medição. O ConnectML consome estes arquivos para extrair o veredito da peça.
- **Inbound (Monitoramento)**: Refere-se à rotina executada pelo `FileWatcherService`, que observa ativamente um diretório configurado em busca de novos arquivos `.QIF` recém-gerados pelas máquinas de medição.
- **Parsing**: O processo (`QifParser`) que abre o arquivo XML, localiza o nó da medição mais recente e deduz o status da peça (`PASS` ou `FAIL`).
- **Outbound (Destino)**: O local para o qual o resultado da medição é despachado. Atualmente suportado:
  - **S7Comm (PLC)**: Escrita direta em blocos de dados (DB) da Siemens utilizando TCP/IP (ISO-on-TCP).
  - **Webhook (JSON)**: Possibilidade de enviar os resultados via HTTP POST para um endpoint configurado (conforme escopo futuro/existente).
- **Simulator (`ConnectML.Simulator`)**: Uma aplicação de console auxiliar que abre a porta 102 localmente, respondendo ao Handshake do protocolo ISO-on-TCP. Utilizado para emular um PLC Siemens durante o desenvolvimento sem a necessidade do hardware físico.

---

## 2. Componentes Críticos e Camadas

- **`ConnectML.UI`**: A camada de Apresentação em WPF. Atualmente as views (`MainWindow.xaml`) instanciam a orquestração via DataBinding (`MainViewModel`).
- **`ConnectML.Core`**: Onde residem as abstrações puras (ex: `IPlcDriver`, `QifParser`). Esta camada **não** deve ter dependências relacionadas à rede ou IO.
- **`ConnectML.Infrastructure`**: Contém as implementações reais que interagem com o mundo externo, notavelmente o `SiemensS7Driver` (que encapsula o S7NetPlus) e o `FileWatcherService`.

---

## 3. Regras de Ouro e "Gotchas" (Importante para IA)

Para evitar alucinações ou introdução de bugs no projeto, siga estas regras estritas durante a escrita de código:

### 3.1. Concorrência e UI Thread
**O Problema**: Eventos advindos de conexões TCP/IP (do S7Driver) ou monitoramento de disco (FileWatcher) disparam em Threads de Background. O WPF lança exceção (cross-thread) se a interface for atualizada fora da Thread principal.
**A Regra**: **Sempre** utilize `Application.Current.Dispatcher.Invoke` ao atualizar propriedades da `ViewModel` que notifiquem a UI (`INotifyPropertyChanged`) ou manipular Coleções (`ObservableCollection`) quando a ação for disparada por um serviço de I/O ou rede.

### 3.2. Normalização de Endereços S7
**O Problema**: Operadores de chão de fábrica configuram na UI os endereços usando notação curta, como `DB10.0` ou `DB20.2`. A biblioteca `S7NetPlus` espera a formatação completa do protocolo, sob pena de erro.
**A Regra**: Toda string de endereço inserida na UI **deve** passar pelo processo de normalização (internamente no Driver) convertendo-a para os formatos aceitos (ex: `DB10.0` para bit -> `DB10.DBX0.0` ou `DB10.2` para palavra -> `DB10.DBW2`). Não envie endereços crus diretamente para a biblioteca subjacente.

### 3.3. Encoding Seguro (Leitura QIF e Arquivos Legados)
**O Problema**: A leitura de arquivos de metrologia legados (e certos XMLs) pode conter símbolos especiais como `Ø` (diâmetro), `°` (graus), e acentuações não suportadas pelo padrão UTF-8 simples.
**A Regra**: Padronize e documente a leitura de fluxos de dados legados utilizando `Encoding.Latin1` (ISO-8859-1) para evitar a corrupção de caracteres silenciosa ao parsear os resultados.

### 3.4. Estética e Tokens (UI)
**O Problema**: Hardcoded colors geram interfaces difíceis de manter e não suportam o Tema Escuro (Dark Mode).
**A Regra**: Durante a evolução das views XAML (especialmente após a Sprint 3), **jamais** insira cores em formato hexadecimal direto (ex: `Background="#FF0000"`). Utilize estritamente referências dinâmicas aos dicionários do Design System (ex: `Background="{DynamicResource Brush.Background.Base}"`).
