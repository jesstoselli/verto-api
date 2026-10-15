# Verto — Design da v1

**Data**: 2026-10-15
**Repos**: `Verto` (API .NET) + `VertoApp` (KMP/CMP, Android primeiro)
**Nome**: do latim *verto* — mudar, transformar, adaptar. O app se adapta ao estado do dia, não o contrário.

## Contexto

Planner pessoal pensado para um cérebro TDAH + AH/SH + TEA, onde as necessidades se contradizem:

- **TEA**: previsibilidade, rotina clara, baixo ruído visual, regras bem definidas.
- **TDAH**: odeia rigidez, cegueira temporal, paralisia por decisão, captura precisa ser instantânea.
- **AH/SH**: quer um sistema profundo, entendível e personalizável.

Hoje existe um planner no Notability (iPad) que funciona por ciclos de ~3 meses e depois é esquecido. Ele já ajudou a criar rotinas, fazer exercício e lembrar de tarefas domésticas. **O problema não é o planner parar de funcionar — é esquecer que ele existe.** O iPad é um passo a mais; o celular está sempre na mão.

## Objetivo e critério de sucesso

- **Sucesso** = continuar usando depois da fase de novidade (≥ 3 meses), e voltar sem culpa quando sumir.
- Missão principal: **aparecer** sem virar ruído (widget, notificação gentil) e **receber de volta** sem culpa (reset).
- Uso pessoal (uma usuária), com sync entre dispositivos desde a v1, porque Mac e iPad vêm depois.

## Escopo

### Entra na v1
- **Despejo (braindump)** em texto: captura em < 3 s pelo widget, tile do Quick Settings, compartilhar e o próprio app.
- **Rotinas elásticas** com Plano A (ideal) e Plano C (mínimo), ancoradas em eventos.
- **Tarefas pontuais** quebradas em micro-passos, com energia por passo.
- **Tunnel Vision**: 1 passo por vez, compatível com a energia atual.
- **Widget "Agora"**, notificações de janela de âncora e prazo real.
- **Reset sem culpa** e tela de volta após ausência.
- **Sync** offline-first entre dispositivos.

### Fica para v2+
- Despejo por voz.
- Regras de automação customizáveis (na v1 só a regra fixa de incubação).
- Visão macro/aérea (objetivos, padrões de hábito).
- Eixo "tipo de carga mental" da matriz de seleção.
- Plano B nas rotinas.
- Targets iOS e macOS.
- Login compartilhado com o BillFolder.

## Modelo de domínio

| Conceito | Campos principais | Regras |
|---|---|---|
| **InboxItem** (despejo) | `text`, `createdAt`, `source` (`widget` \| `tile` \| `share` \| `app`), `state` | Estados: `raw → processed \| incubated \| discarded`. Só `text` é obrigatório. Processar é opcional. |
| **Task** | `title`, `notes?`, `dueDate?`, `dueKind?` (`hard` \| `soft`), `pinnedToToday` | `hard` = prazo real, aparece com destaque gentil. `soft` = "seria bom até", nunca vira atrasado e sai do destaque quando passa. |
| **Step** | `taskId` *ou* (`routineId` + `plan`), `order`, `title`, `energy` (`low` \| `medium` \| `hyperfocus`), `doneAt?` | Micro-passo. Rotinas usam `plan` = `A` \| `C`. |
| **Anchor** | `name`, `windowStart`, `windowEnd` | Evento do dia definido pela usuária ("Acordei", "Almocei"). |
| **AnchorEvent** | `anchorId`, `day`, `triggeredAt`, `source` (`tap` \| `window`) | Marca que a âncora aconteceu naquele dia. |
| **Routine** | `name`, `anchorId`, `weekdays` | Passos do Plano A e do Plano C via `Step`. |
| **RoutineLog** | `routineId`, `day`, `outcome` (`A` \| `C` \| `skipped`) | Plano C conta como sucesso. Sem streak, sem "falhou". |
| **DayEnergy** | `day`, `level` (`low` \| `medium` \| `high`), `setAt` | Pode mudar a qualquer momento. Vale o último valor. |

Todas as entidades têm `id` (UUID gerado no cliente), `updatedAt` e `deletedAt?` (soft delete) para o sync.

### Regras de domínio

- **Dia lógico**: o dia vira num horário configurável, padrão **04:00**. Tudo que usa `day` usa o dia lógico.
- **Disparo de âncora**: o que vier primeiro entre (a) o toque da usuária ("Almocei") e (b) a abertura da janela. Se ela tocar antes, a notificação da janela não sai. Um `AnchorEvent` por âncora por dia.
- **Hoje** é uma visão derivada, não uma lista armazenada:
  1. rotinas do dia (pelo `weekdays`), agrupadas por âncora, em ordem de janela;
  2. tarefas com `pinnedToToday`, com limite suave de 3 (o app avisa com gentileza, não bloqueia);
  3. tarefas `hard` com prazo nos próximos 3 dias.
- **Sugestão de plano**: com `DayEnergy = low`, as rotinas do dia sugerem o Plano C. A usuária pode trocar de plano a qualquer momento.
- **Registro da rotina**: o `RoutineLog` do dia é gravado quando todos os passos de um plano são concluídos (`A` ou `C`; se os dois, vale `A`) ou quando ela toca "pular" (`skipped`). Rotina sem registro no fim do dia lógico fica sem registro, sem inferir falha.
- **Tunnel Vision** escolhe 1 passo pendente:
  1. filtro por energia do dia: `low` → só passos `low`; `medium` → `low` e `medium`; `high` → todos, incluindo `hyperfocus`;
  2. prioridade: passo da rotina da âncora já disparada (do plano sugerido: C com energia `low`, A nos outros casos) → próximo passo de tarefa `hard` → próximo passo de tarefa fixada em hoje;
  3. "Outro" pula para o próximo candidato sem registrar nada negativo;
  4. "Tá grande demais" abre a quebra do passo ali mesmo.
  Sem candidato, mostra uma mensagem calma ("Nada pra agora. Tá tudo bem.").
- **Incubação automática**: despejo `raw` há mais de 3 dias vira `incubated`, sem notificação.
- **Reset** (um botão, sem confirmação dramática):
  - `pinnedToToday = false` em todas as tarefas;
  - despejos `raw` viram `incubated`;
  - nada é marcado como atrasado nem apagado.
- **Volta após ausência**: ao abrir o app depois de N dias sem uso (padrão 4, configurável), antes de qualquer lista aparece: "Que bom te ver. Quer começar com um dia limpo?" com **[Reset]** e **[Ver como estava]**.
- **Linguagem**: nenhum contador de pendências, nenhum vermelho de alerta, a palavra "atrasado" nunca aparece. Prazos `hard` vencidos aparecem em tom neutro ("prazo era dia 12").

## Telas e superfícies

### Navegação (3 abas)
1. **Hoje**: chip de energia no topo; rotinas por âncora, só a próxima expandida; tarefas fixadas; prazos reais próximos; botão grande **"Só 1 coisa"**.
2. **Inbox**: lista de despejos. Gestos: virar tarefa, incubar, descartar. Seção recolhida "Incubados". Sem contador.
3. **Planejar**: listas e editores de Tarefas (passos + energia), Rotinas (âncora, dias, Plano A, Plano C) e Âncoras (nome + janela).

Mais: **Tunnel Vision** em tela cheia (Feito / Outro / Tá grande demais / Sair) e **Ajustes** (virada do dia, dias de ausência, horário de silêncio, conta, estado do sync).

### Superfícies Android
- **Widget "Agora"** (Glance): próxima âncora e passo atual, botão da âncora, botão "+ Despejar".
- **Tile no Quick Settings**: abre a captura por cima de qualquer app.
- **Share target**: texto compartilhado vira despejo.
- **Notificações**: só dois tipos (janela de âncora e prazo `hard`). No máximo 1 por âncora por dia. Respeitam o horário de silêncio. Nunca dizem "você tem X pendências".

### Visual
Paleta calma com modo escuro, sem vermelho de alerta, tipografia grande, muito espaço em branco, animações mínimas e respeito ao "reduzir movimento" do sistema.

## Arquitetura

**Onde mora a lógica**: regras de negócio no cliente, em Kotlin comum, porque o app precisa funcionar sem rede. O servidor guarda, autentica, valida formato e sincroniza.

### App — `VertoApp` (KMP + CMP)
- **`shared/domain`**: Kotlin puro. Entidades e regras (Hoje, Tunnel Vision, incubação, reset, âncoras, dia lógico). Sem dependência de plataforma.
- **`shared/data`**: Room KMP (banco local), Ktor Client (rede), kotlinx-datetime, motor de sync.
- **`composeApp`**: telas em Compose Multiplatform, navegação, ViewModels, Koin.
- **`androidMain`**: widget Glance, tile, share target, AlarmManager inexato para janelas de âncora, WorkManager para sync.
- **Targets**: Android + JVM desde o início (JVM roda os testes e impede vazamento de API Android no código comum). Pacote base `com.verto`.

### API — `Verto` (.NET 10)
- Mesmo Clean Architecture do BillFolder: `Verto.Domain`, `Verto.Application`, `Verto.Infrastructure`, `Verto.Api`.
- Auth copiado do BillFolder (JWT HS256, refresh-token families com rotação, Argon2id). Sem dependência entre os dois projetos.
- Endpoints: `/v1/auth/*`, `GET /v1/sync/pull?since=<version>`, `POST /v1/sync/push`, `GET /v1/health`.

### Sync
- **Cliente**: toda escrita vai para o Room e para uma tabela **outbox** na mesma transação. O sync roda ao abrir o app, alguns segundos depois de cada mudança e periodicamente (WorkManager).
- **Push**: envia os registros do outbox (registro inteiro, não diff). O servidor faz upsert por `id` + `user_id` e atribui `server_version` de uma sequência global do Postgres.
- **Pull**: devolve registros com `server_version > since`, incluindo os apagados (`deleted_at`). O cliente aplica e guarda o maior `server_version` como cursor.
- **Conflito**: vale a última escrita que chegou no servidor. Pull aplicado sobre registro com mudança local ainda no outbox mantém a versão local, que será enviada no próximo push.
- **Falhas**: nunca bloqueiam a UI. Retry com backoff. Estado aparece só em Ajustes.

## Infra

- **Hetzner**: mesma VPS CX23 do BillFolder. Mais um container `verto-api` no compose.
- **Postgres**: mesmo servidor, database `verto` com usuário próprio, sem acesso ao database do BillFolder.
- **nginx**: server block novo com Let's Encrypt.
- **Cloudflare**: DNS do subdomínio da API. Domínio: `api.verto.<tld>` se um domínio próprio for comprado; senão `verto-api.billfolder.app`. Decidir na hora do deploy.
- **CI/CD**: workflow copiado do BillFolder (build, test, push no GHCR, deploy por SSH).
- **Backup**: `pg_dump` diário dos databases `verto` e do BillFolder, com retenção de 14 dias.

## Testes

- **Domínio** (`commonTest`, roda na JVM): dia lógico, derivação do Hoje, seleção do Tunnel Vision, sugestão de Plano C, disparo de âncora (toque vs janela), incubação, reset, volta após ausência.
- **API**: testes de integração com Postgres real cobrindo auth, push, pull, cursor, conflito e soft delete. Isolamento por usuário.
- **Cliente**: motor de sync contra servidor fake (outbox, retry, aplicação do pull, conflito local pendente).

## Ordem sugerida de construção

1. API: esqueleto + auth copiado + sync genérico + deploy.
2. App: domínio + banco local + telas Hoje, Planejar e Inbox, funcionando offline.
3. App: motor de sync ligado à API.
4. App: Tunnel Vision, reset e volta após ausência.
5. App: widget, tile, share target e notificações.

Os detalhes ficam para o plano de implementação.
