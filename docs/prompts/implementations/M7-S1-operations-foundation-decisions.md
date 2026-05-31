# M7-S1: Operations Foundation Decisions — Auth Posture (ADR-024) + OpenSpec Adoption + Read-Model Field Freeze

**Milestone:** M7 ([Operations BC](../../milestones/M7-operations-bc.md))
**Slice:** S1 of 7 (foundation slice; no BC project is created here — that is S2)
**Narrative:** `docs/narratives/008-operator-resolves-dispute-with-extension.md` (foundation slice; the narrative is the operator-vantage spec for the dispute/escalation queue, but its Operations read model — `OperationsObligationsView` — is built in S4. S1 freezes the field set that surface depends on.)
**Agent:** @PSA (with @PO consulted on the auth-scheme MVP shape and the OpenSpec adopt/decline/defer call)
**Estimated scope:** one PR; ~1 ADR + 1 source-audit artifact + ledger edits + the M7-S1 retro. No `CritterBids.Operations` project, no handlers, no read-model code, no `Program.cs` change.

---

## Goal

Land the foundation decisions the Operations BC's implementation slices (S2–S6) depend on, so no later slice stops to escalate an architecture question mid-flight. This session makes three decisions and records them durably:

1. **The authentication-posture resumption** — the headline M7 decision — recorded as **ADR-024**. After five milestones of `[AllowAnonymous]`, M7 reintroduces real authorization on staff surfaces. The ADR settles the scheme, the `StaffOnly` policy, which existing endpoints change, how the Operations query endpoints and Relay's `OperationsHub` are authorized, and the test strategy. This is broader than "add `[Authorize]`": `Program.cs` today calls bare `AddAuthentication()` / `AddAuthorization()` with **no default scheme**, so a naive attribute flip fails at runtime. The ADR decides what gets wired so it does not.
2. **The OpenSpec adopt/decline/defer call for Operations** (`operator-dashboards` capability) per ADR 021's per-BC opt-in, recorded in the retro and the `openspec/README.md` ledgers (the fork is listed as an Open Question in this prompt).
3. **The read-model field freeze** — a lightweight Operations source-audit / mini event-modeling pass that locks the field shape of every M7 operator read model (settlement queue, lot board, bid-activity feed, `OperationsObligationsView`, session/participant activity board) following ADR 014 Path A, classifying each as an upsert view or an append/feed view. No Operations workshop exists (only narrative 008), so this artifact is the frozen spec S2–S5 build against.

This slice is structurally equivalent to M5-S1 and M6-S1 (both foundation-decisions slices that created no BC project). The design work it does is decisions and a spec artifact — no Operations-BC code. That begins in M7-S2.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M7-operations-bc.md` | Authoritative for scope. §1 exit criteria, §2 "Auth posture resumption" + "Staff-command ownership boundary" + the enumerated consumer table, §6 Conventions Pinned, §7 slice table (S1 row). Do not re-decide anything the milestone already pinned. |
| `CLAUDE.md` | Routing + global conventions (`sealed record`, `IReadOnlyList<T>`, no "Event" suffix, no "paddle"; the `[AllowAnonymous]` "through M6" pin that M7 is the milestone to lift) |
| `docs/narratives/008-operator-resolves-dispute-with-extension.md` | The operator-vantage spec; its Cast/Moments name the `OperationsObligationsView` fields the escalation + open-dispute queues need (jointly authoritative per AUTHORING.md rule 3) |
| `docs/vision/bounded-contexts.md` (§Operations, §Integration Topology) and `docs/vision/domain-events.md` (§Operations + the per-BC event payloads) | The event sources the read-model field freeze audits — the topology and payload catalog every field must trace back to |
| `src/CritterBids.Contracts/**` (the Operations-consumed event records named in the milestone §2 consumer table) | The authoritative field source for the freeze — trace every read-model field to a real contract payload, not to a doc that may be stale |
| `docs/decisions/014-cross-bc-read-model-extension-shape.md` + `docs/decisions/023-relay-reactive-broadcast-architecture.md` | Path A (sibling-handler-per-source-BC upsert) — the read-model strategy S1 confirms; ADR 023 fixes `OperationsHub` ownership in Relay, informing the hub-auth portion of ADR-024 |
| `docs/decisions/README.md` (confirm next unreserved = **024**) + `docs/decisions/021-openspec-cli-for-m6.md` + `openspec/README.md` | The next-ADR-number check, the per-BC OpenSpec opt-in rule, and the capability + adoption ledgers this slice updates |

*(The current bare `AddAuthentication()` / `AddAuthorization()` "no default scheme" state in `src/CritterBids.Api/Program.cs` is the fact ADR-024 must address; verify it while authoring the ADR. S1 does not edit `Program.cs`.)*

## In scope

1. **ADR-024 — authentication-posture resumption.** Author `docs/decisions/024-<slug>.md`, status `Accepted`. The decision must settle, with rationale and alternatives weighed:
   - **The authentication scheme.** The milestone pins the MVP target as a config-driven staff passphrase (a demo token / header credential bound from configuration). The in-session decision is the **concrete transport** for it — request header / demo token vs cookie — registering a concrete default scheme so authorization has something to challenge against (the fix for the current no-scheme state). Do not widen beyond the config-passphrase MVP posture unless @PO explicitly records a milestone-level deviation.
   - **The `StaffOnly` authorization policy** — confirm the name and define what claim/credential satisfies it.
   - **Which existing endpoints gain `StaffOnly`** and which participant-facing endpoints stay `[AllowAnonymous]` for MVP. The staff-mutation endpoints (`ResolveDisputeEndpoint` in Obligations, `WithdrawListing` in Selling, `CreateSession` / `StartSession` in Auctions) stay in their owning BCs and only have the policy applied — they are **not** re-homed into Operations. (Applying the policy is S6 work; S1 decides the set.)
   - **How the Operations staff query endpoints and Relay's `OperationsHub` are authorized** — including staff-group membership on the hub (ADR 023: the hub stays in Relay).
   - **The test strategy** — authorized-vs-unauthorized assertions (401/403 without a valid staff credential, 200 with it) run against a real Kestrel host + Testcontainers, because a naive `[Authorize]` flip with no registered scheme fails at runtime and Alba's in-memory `TestServer` cannot drive a SignalR `HubConnection`.
   - Update `docs/decisions/README.md` index row + next-unreserved-number pointer.
2. **OpenSpec adopt/decline/defer decision for Operations** (`operator-dashboards` capability). Make the call (adopt = Obligations path; decline = Relay path, proceed under ADR 020 alone; defer = revisit at Operations' first complex change), record the rationale in the retro, and flip the Operations rows in `openspec/README.md`'s capability ledger (`proposed` → `confirmed`) and per-BC adoption ledger (`⏸ evaluate at opening` → the chosen outcome). The fork itself is listed as an Open Question below; the binding record is the retro plus the ledger edits.
3. **Read-model field freeze (source-audit artifact).** Produce a durable artifact that, for each M7 operator read model — settlement queue, lot board, bid-activity feed, `OperationsObligationsView`, session/participant activity board — lists its fields, classifies it as an **upsert view** (settlement queue, lot board, `OperationsObligationsView`, session/participant activity) or an **append/feed view** (bid-activity feed), names the source event(s) per ADR 014 Path A sibling-handler family, and traces every field to an existing `CritterBids.Contracts` event payload (no new contract types — Operations is a pure consumer). The exact home of this artifact (a new `docs/workshops/006-operations-source-audit.md`, an appendix section in the milestone doc, or a `docs/skills/`-adjacent note) is an Open Question below — pick the location the S2–S5 prompts will look in first and record the choice in the retro.
4. **Confirm ADR 014 Path A as the read-model build strategy** for Operations (no Marten multi-stream event projections, since the inbound firehose is not appended to local Operations streams per the milestone's §3 non-goal). This is a confirmation, not a new ADR — record it in the source-audit artifact.
5. `docs/retrospectives/M7-S1-operations-foundation-decisions-retrospective.md` — written last; carries the `**Prompt:**` header line, the `## Spec delta — landed?` paragraph, the OpenSpec-decision closure note, and the recorded home of the field-freeze artifact.

## Explicitly out of scope

- `CritterBids.Operations` and `CritterBids.Operations.Tests` projects, `AddOperationsModule()`, Marten `operations`-schema config — **S2**
- Any consumer handler, read-model document class, or staff query endpoint — **S2–S6** (S1 freezes the field set; it writes no read-model code)
- `Program.cs` edits of any kind: the auth scheme registration, the `StaffOnly` policy registration, and the `operations-*` consumer/publish routes are **implementation** and land in their slices (auth in S6; routes in S2–S5). S1 only *decides* their shape.
- Applying `StaffOnly` to the existing staff-mutation endpoints, or touching Obligations / Selling / Auctions code — **S6**
- Flipping any participant-facing endpoint off `[AllowAnonymous]` — the ADR records the MVP stance (participant surfaces stay anonymous), but no endpoint changes this slice
- Full staff identity / per-user accounts / roles beyond staff-vs-anonymous / an identity provider — post-MVP per the milestone §3
- The `OperationsHub` staff-group-targeting refinement — S1 only decides whether it is in M7 scope at all (Open Question); if scoped in, it is a Relay edit in S6, not S1
- The React ops dashboard SPA (**M8**), the `DemoResetInitiated` cascade (post-MVP), and any upstream BC behavior change
- Running `/opsx:propose` or scaffolding an `openspec/changes/<slug>/` folder — even if the decision is "adopt", the change folder is authored when S2 opens, not here. Do not edit OpenSpec-managed files under `.github/prompts/` or `.github/skills/`.

## Conventions to pin or follow

- **Auth posture:** ADR-024 is the first encoding of the `StaffOnly` policy and the staff-authentication scheme; it supersedes the `[AllowAnonymous]`-through-M6 pin in `CLAUDE.md` (note the `CLAUDE.md` update as a follow-on the ADR records, even though the convention's code application is S6).
- **Read-model strategy:** ADR 014 Path A owns the sibling-handler-per-source-BC upsert pattern; the source-audit artifact points at it and does not restate it.
- **Staff-command ownership boundary** (milestone §6): mutations stay in their owning BC; Operations never re-homes or directly emits another BC's events. The ADR's endpoint list reflects this.
- **Operations is a pure consumer:** the field freeze must not introduce any `CritterBids.Contracts.Operations.*` type — every field traces to an existing upstream event payload.
- Contract / record conventions follow the global `sealed record` + `IReadOnlyList<T>` + no-"Event"-suffix + no-"paddle" rules where the artifact names payload fields.

## Spec delta

Per ADR 020, this slice's spec consequence is the **authoring of ADR-024** (a new governing architectural decision: the staff-auth scheme + `StaffOnly` policy + endpoint set + hub auth + test strategy) and the **read-model field freeze artifact** (the canonical field shape S2–S5 implement against, tracing narrative 008's operator surface and `domain-events.md` §Operations into concrete view fields). The OpenSpec adopt/decline/defer decision determines whether Operations' later slices also carry an `openspec/changes/<slug>/` spec home — that decision is itself part of this slice's spec consequence, recorded in the `openspec/README.md` ledgers. No read-model behavior is implemented; the saga/handler/endpoint requirements remain unimplemented until S2+. The retro's `## Spec delta — landed?` paragraph confirms ADR-024 exists and is indexed, the field-freeze artifact exists at its chosen home, and the OpenSpec ledgers reflect the decision.

## Acceptance criteria

- [ ] `docs/decisions/024-<slug>.md` exists, status `Accepted`, settling: scheme, `StaffOnly` policy + satisfying credential, the staff-endpoint set that changes (and the participant set that stays anonymous), `OperationsHub`/query-endpoint authorization, and the real-Kestrel + Testcontainers test strategy
- [ ] `docs/decisions/README.md` index gains the ADR-024 row and the next-unreserved-number pointer advances to 025
- [ ] OpenSpec adopt/decline/defer recorded for Operations; `openspec/README.md` capability ledger (`operator-dashboards`) and per-BC adoption ledger both reflect the decision
- [ ] No `openspec/changes/<operations-slug>/` folder created and no `/opsx:propose` run (the change folder, if any, is S2 work even when the decision is "adopt")
- [ ] Read-model field-freeze artifact exists at a recorded location, covering every M7 operator read model (settlement queue, lot board, bid-activity feed, `OperationsObligationsView`, session/participant activity board), each classified upsert vs append/feed, each field traced to an existing `CritterBids.Contracts` event payload, with ADR 014 Path A confirmed as the build strategy
- [ ] No `CritterBids.Operations` / `CritterBids.Operations.Tests` project; no handler, read-model, or endpoint code; no `Program.cs` change
- [ ] No new `CritterBids.Contracts.*` type introduced
- [ ] Docs-only change — no `dotnet build` / `dotnet test` required (no code touched); existing test baseline unchanged
- [ ] `docs/retrospectives/M7-S1-operations-foundation-decisions-retrospective.md` written with the `**Prompt:**` header and `## Spec delta — landed?` paragraph
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

- **Auth scheme shape.** Confirm the milestone's working assumption (config-driven staff passphrase) with @PO, or record the chosen alternative. Is the credential a request header / demo token, a cookie, or a bearer token bound from configuration? Decide in-session; do not assume beyond the milestone's stated default.
- **OpenSpec for Operations.** Genuine fork — adopt, decline, or defer. Weigh: Operations is a pure-consumer read-model BC with no saga and no published contracts (lighter SHALL-surface than Obligations), and Relay already declined. Make the call in-session with @PO; record the rationale.
- **Field-freeze artifact home.** New `docs/workshops/006-operations-source-audit.md` (next workshop number is 006), a milestone-doc appendix, or a `docs/skills/`-adjacent note? Pick where the S2–S5 prompts will look first.
- **`OperationsHub` staff-group targeting in M7 or deferred?** The milestone leaves the M6-S6 `Clients.All` → staff-group refinement as an M7-S1 scope call. Decide whether it is in M7 (a Relay edit in S6) or deferred; record the call.
- **Thin Operations-side query/launch convenience seam.** The milestone leaves open whether the dashboard gets a thin Operations-side seam over the owning-BC staff mutations or calls those endpoints directly (the default). Decide the scope stance so S6 knows whether to build one.
