# Questionário de Integração: ConnectML <-> EGA

Bem-vindos à integração com o middleware **ConnectML**.
Para avançarmos com a integração bidirecional (via API REST/JSON), precisamos alinhar alguns detalhes técnicos sobre como o fluxo de dados ocorrerá entre a plataforma **EGA** e o ConnectML.

Por favor, revisem os tópicos abaixo e nos confirmem o modelo ideal para a comunicação.

---

## 1. Fluxo de Entrada (EGA -> ConnectML)
Neste cenário, a EGA informará ao ConnectML que uma nova Ordem de Produção / Inspeção deve ser iniciada. O ConnectML, por sua vez, repassará essa notificação para o software de medição (MeasurLink).

### 1.1 Direção da Comunicação
Como vocês preferem que o gatilho "GO" seja disparado?
- [ ] **A EGA atua como Cliente (PUSH):** A EGA fará uma requisição HTTP `POST` num endpoint exposto pelo ConnectML (ex: `http://<ip-maquina-connectml>:5000/api/trigger/go`).
- [ ] **O ConnectML atua como Cliente (PULL):** O ConnectML fará o *polling* (ex: GET a cada 5 segundos) em um Endpoint exposto pela EGA para perguntar "Tem ordem nova?". *(Nota: O modelo PUSH é mais performático).*

### 1.2 Formato do Payload (Comando Iniciar)
Se utilizarmos o modelo PUSH (EGA faz o POST), precisamos confirmar os nomes exatos das propriedades (chaves) do JSON.

**Exemplo Proposto pelo ConnectML:**
```json
{
  "comando": "GO",
  "rotina": "Eixo_Dianteiro_V2",
  "ordemProducao": "OP-2023-9988",
  "cicloSegundos": 45
}
```
**Perguntas:**
1. A EGA suporta enviar este modelo ou vocês já possuem um padrão/nomenclatura imutável no sistema de vocês? Se já possuírem, favor enviar um exemplo do JSON que será disparado.
2. Haverá necessidade de método de autenticação (ex: Header `Authorization: Bearer <token>`, ou `x-api-key`) ou sendo redelocal a API do ConnectML pode rodar aberta?

---

## 2. Fluxo de Saída (ConnectML -> EGA)
Ao término da medição física, o MeasurLink gera um arquivo de Qualidade. O ConnectML o extrai os principais pontos e notifica a EGA sobre a situação da medição.

### 2.1 Envio dos Resultados
O ConnectML fará um HTTP `POST` ou `PUT` transferindo os resultados da referida "Ordem/Rotina" para a EGA.

**Perguntas:**
1. Qual a URL (Endpoint) do servidor da EGA onde devemos realizar este POST?
2. Que tipo de autenticação o servidor da EGA nos exigirá?

### 2.2 Formato do Payload de Resultados
Abaixo está o exemplo proposto contendo o _Status da Medição_, _Rotina_, _Corrida_, e _Evento Estatístico_. 

**Exemplo Proposto pelo ConnectML:**
```json
{
  "ordemProducao": "OP-2023-9988",
  "rotina": "Eixo_Dianteiro_V2",
  "dataFinalizacao": "2023-11-05T14:30:00Z",
  "statusGeral": "NOK",
  "pecasInspecionadas": 1,
  "caracteristicasFalhas": 2,
  "eventoEstatistico": "Tendencia_Superior"
}
```

**Perguntas:**
1. Este layout atende a necessidade de apontamento de vocês? Haveria alguma divergência nos nomes das chaves (ex: Mudar `statusGeral` para `ResultOee`)?
2. Em relação ao "Evento Estatístico" (`eventoEstatistico`): Se o ConnectML enviar textos padrões (ex: `Tendencia_Superior`, `Controle_OK`, `Fora_Tolerancia`), isso é interpretado corretamente pelo painel de vocês ou ele espera IDs ou *Enums* numéricos (ex: `1`, `2`, `3`)?

---

**Muito obrigado!**
Assim que essas definições estiverem preenchidas, o time de desenvolvimento poderá concluir as configurações da API Local do ConnectML.
