# Implementation Plan: Sprint 0 (Revisão da Documentação Principal)

Conforme a solicitação de alinhamento com a situação atual da aplicação, procedemos com o pente fino nos arquivos principais após a realocação das pastas históricas.

> [!IMPORTANT]
> **User Review Required**
> Abaixo estão os achados da auditoria nos documentos principais. Valide se concorda com a avaliação (especialmente sobre a ausência de dois guias) para que eu possa aplicar as alterações diretamente nos arquivos.

## Auditoria dos Documentos (Achados)

1. **`README.md`**
   - *Status:* **Desatualizado**. 
   - *Problema:* A seção "Documentação" aponta para links quebrados como `docs/EGA_API_REQUIREMENTS.md` e `docs/PHASE2_IMPLEMENTATION_PLAN.md` (que foram movidos e renomeados). Além disso, não cita o recém-criado `AI_CODEBASE_MAP.md` nem o playbook `METHODOLOGY_TRANSFER.md`.

2. **`ARCHITECTURE.md`**
   - *Status:* **Acurado (mas necessita de pequenas adições)**.
   - *Problema:* O fluxo Híbrido, o uso do `SiemensS7Driver` e do `QifParser` descritos no documento ainda refletem o codebase atual. Sugiro apenas adicionar uma seção referenciando que a Arquitetura de Plugins e novos Padrões Estéticos estarão em desenvolvimento nas próximas Sprints (conforme Roadmap).

3. **`DEPLOYMENT_VELOPACK.md`**
   - *Status:* **Acurado**.
   - *Problema:* Os comandos (`vpk pack --packId ConnectML...`) e os caminhos estão perfeitamente alinhados. Nenhuma alteração drástica necessária.

4. **`AI_CODEBASE_MAP.md`**
   - *Status:* **Novo / Atualizado**.
   - *Problema:* Acabou de ser gerado por mim e já possui a visão estrita atual do domínio do ConnectML (Inbound, Parsing, Outbound PLC).

5. **`PLUGIN_GUIDE.md`** e **`SERIAL_CONFIGURATION_GUIDE.md`**
   - *Status:* **Não Encontrados**.
   - *Problema:* Estes arquivos **não existem** no repositório atual do ConnectML. É muito provável que eles sejam artefatos remanescentes da documentação do *SelectML* que não foram transferidos durante o fork/inicialização deste repositório.

## Proposed Changes

- **[MODIFY]** `README.md`: Atualizar todos os links de documentação, removendo referências mortas da "Fase 2" e apontando para as novas rotas.
- **[MODIFY]** `docs/ARCHITECTURE.md`: Inserir um discreto adendo apontando para `METHODOLOGY_TRANSFER.md` no tocante ao futuro carregamento dinâmico de drivers (Plugins).

## Open Questions

- Como não encontrei o `PLUGIN_GUIDE.md` nem o `SERIAL_CONFIGURATION_GUIDE.md`, deseja que eu inicie a redação deles com base no playbook do SelectML, ou podemos ignorá-los nesta Sprint já que a funcionalidade de plugins só será implementada de fato na Sprint 2?
