# Handoff — Un-stale the `wolverine-signalr` skill against ADR-023

**For:** the next session that corrects the backend SignalR skill.
**Status:** problem identified, **not yet fixed**, nothing committed.
**Target file:** `docs/skills/wolverine-signalr/SKILL.md`
**Authority:** `docs/decisions/023-relay-reactive-broadcast-architecture.md` (Accepted 2026-05-29)
**Client companion (already correct):** `.claude/skills/signalr/SKILL.md`

This is **not** an implement-a-slice prompt. It is a focused doc-hygiene fix: one skill file contains a
client snippet and a pitfall entry that contradict the accepted ADR. Correct them, keep everything that
is still right, and don't over-reach into the unresolved frontend question.

---

## 0. Read this first

CritterBids real-time has a **server side** (`docs/skills/wolverine-signalr/`, this file) and a **client
side** (`.claude/skills/signalr/`, authored in the prior session and already consistent with ADR-023).
The server skill is **internally contradictory**: parts of it describe the Wolverine SignalR *transport*
(CloudEvents envelope, `ToSignalR`, `WolverineHub`) — call this **path (a)** — while other parts
correctly describe the **plain-`Hub` + direct `IHubContext` push** that the Relay BC actually uses —
**path (b)**. **ADR-023 chose path (b) and explicitly rejected path (a).** The stale spots are the
path-(a) remnants that tell the *React client* to unwrap a CloudEvents envelope that does not exist.

The whole repo convention: **the ADR is the authority**; the skill must match it.

---

## 1. What ADR-023 actually decided (the contract to enforce)

From ADR-023 §Decision:

- `BiddingHub` / `OperationsHub` are **plain `Microsoft.AspNetCore.SignalR.Hub` subclasses**, mapped with
  `app.MapHub<…>(…)`, served by `services.AddSignalR()` — **not** `opts.UseSignalR()` / `ToSignalR()`.
- Handlers inject `IHubContext<THub>` and call `SendAsync("ReceiveMessage", notification, ct)` directly.
- **"The raw notification record is the `ReceiveMessage` payload (no CloudEvents envelope — that wrapper
  is a path-(a) transport behaviour). Clients deserialize the record directly."**
- `WolverineFx.SignalR` stays referenced (future-door for Option C) but its transport registration is
  **not** used.

So: **no envelope, no `cloudEvent.type`, no `cloudEvent.data`, no `.split(".").pop()`** on the client.

---

## 2. Stale spots to fix (verify line numbers — they will have shifted)

Read the file fresh; do not trust these line numbers.

1. **§"CloudEvents type format — CritterBids finding"** (was ~L46–72). This whole section is path-(a):
   - It claims "Wolverine wraps outbound hub messages in a CloudEvents envelope" and the JS client "must
     read `cloudEvent.data` … and inspect `cloudEvent.type`." Under path (b) this is **false** — the
     handler argument *is* the record.
   - The `WolverineMessageNaming` `type`-shape table (`WebSocketMessage` / marker-interface FQN /
     `[MessageIdentity]`) only governs path-(a) envelope naming. It does **not** apply to CritterBids'
     pushes.
   - The `connection.on("ReceiveMessage", cloudEvent => …)` example with `.split(".").pop()` is the
     **exact anti-pattern** the client skill now warns against. A frontend dev copying it gets `undefined`
     on every branch.
   - **Action:** replace this section with the correct raw-record contract, or remove it and point to
     `.claude/skills/signalr/SKILL.md` + ADR-023. **Decision D1 below** covers what (if anything) to keep
     from the naming table.

2. **§"Common pitfalls"** entry "Expecting kebab-case `type` with marker interfaces … split on `.` in the
   JS client" (was ~L198). This is itself the stale assumption — it tells readers to *do* the wrong thing.
   **Action:** replace with the correct pitfall: *"Expecting a CloudEvents envelope on the client.
   ADR-023 path (b) delivers the raw record on `ReceiveMessage`; there is no `.type` / `.data`. See
   `.claude/skills/signalr/`."*

3. **Frontmatter `tags` and `description`** (was ~L3–5) mention `cloudevents`. Re-check after the body
   edit so the description doesn't still advertise envelope behavior the file no longer teaches.

4. **Full re-read for residual envelope assumptions.** Grep the file for `cloudEvent`, `CloudEvents`,
   `envelope`, `.data`, `.type`, `split`. Anything implying the client unwraps an envelope is stale.
   Leave the genuinely-correct path-(b) content (the "Lived Relay update (M6)" section, the
   `IHubContext`/`SendAsync` handler example, group keys, ADR-024 auth, testing posture) **untouched** —
   it already matches ADR-023.

---

## 3. Do NOT over-reach — the one thing to leave open

Under path (b) there is **no envelope `type`**, so *how the client distinguishes one notification record
from another on the single `ReceiveMessage` method* is **genuinely unresolved**. That discriminator (a
field on the record vs. distinct hub-method names) **plus its Zod schema** is **M8-S3 live-bidding work,
to be recorded in ADR-014.** The client skill already flags this as an open question.

**Do not invent a discrimination scheme in this fix.** This skill is server-side; it should state the
contract (raw record on `ReceiveMessage`) and defer client discrimination to `.claude/skills/signalr/` +
ADR-014. If you find yourself designing the client switch statement, stop.

---

## 4. Decision to surface to the user (with a recommendation)

- **D1 — the `WolverineMessageNaming` type-shape table.** It is accurate generic knowledge *for path (a)*,
  which CritterBids does not use but keeps a future-door to (Option C: `WolverineHub` +
  `opts.UseSignalR<THub>()`).
  - (a) **Delete it** — it is not how CritterBids works and risks re-confusing readers. *(recommended)*
  - (b) **Relocate it** under an explicit "*If a future slice reopens Option C (transport-routed hubs),
    naming works as follows*" conditional, clearly fenced off from the path-(b) reality.
  - (c) Keep as-is. *(not recommended — this is the source of the confusion.)*

Put D1 to Erik before finalizing.

---

## 5. Constraints

- **Docs-only change.** No `.cs` / `.csproj` / build files. The CI path-filter skips build/test for
  doc-only diffs, so no `dotnet build`/`dotnet test` is owed — but say so explicitly in the PR/retro.
- **Branch first; never commit to `main`.** No `Co-Authored-By` trailer.
- **Em-dash hygiene does not apply** (internal doc).
- **Keep the two SignalR skills consistent.** After editing, the server skill and
  `.claude/skills/signalr/SKILL.md` must tell the same story about the wire contract. Cross-link them.
- One-session-one-PR; write a short retro if the session convention calls for it.

---

## 6. Read these first next session

- `docs/decisions/023-relay-reactive-broadcast-architecture.md` — the authority (path (a) vs (b),
  Options A/B/C, the "no envelope" sentence).
- `docs/skills/wolverine-signalr/SKILL.md` — the file to fix (read in full; line numbers above are stale).
- `.claude/skills/signalr/SKILL.md` — the client companion; mirror its correct contract and its
  staleness warning so the two agree.
- `docs/decisions/013-frontend-core-stack.md` §"Real-time client library" + Deferred Questions — confirms
  the integration pattern (incl. client discrimination) is ADR-014, not yet authored.
- `client/bidder/src/useBiddingHub.ts` — the real client hook; its comments already state the path-(b)
  raw-record contract.

---

## 7. Acceptance criteria

- [ ] No remaining text in `wolverine-signalr/SKILL.md` tells the client to read `cloudEvent.type` /
      `cloudEvent.data` or `.split(".").pop()`.
- [ ] The wire contract is stated correctly: **raw notification record on `ReceiveMessage`, no CloudEvents
      envelope (ADR-023 path b)**, with the ADR cited.
- [ ] The "Common pitfalls" envelope entry is corrected (no longer instructs the wrong behavior).
- [ ] Client message-type discrimination is **deferred to `.claude/skills/signalr/` + ADR-014**, not
      invented here.
- [ ] D1 resolved with Erik; the naming table deleted or clearly fenced as path-(a)/Option-C only.
- [ ] Server and client SignalR skills cross-link and agree.
- [ ] Path-(b)-correct sections (Lived Relay update, handler example, group keys, ADR-024 auth, testing)
      left intact.
- [ ] On a branch, not `main`; PR notes the doc-only/no-test rationale.
