# M7 — Operations BC

**Status:** Planned (opening session: M7-S1 foundation decisions; not yet started)
**Scope:** Operations BC — the staff-facing read-model and dashboard backend that aggregates cross-BC integration events into operator views (lot board, settlement queue, obligation/dispute pipeline, session and participant activity), exposes staff query endpoints, and is the milestone where the `[Authorize]` posture resumes after five milestones of `[AllowAnonymous]`. Operations is the eighth and final MVP BC, and the only one that has never existed in `src/`.
**Companion docs:** [`../vision/bounded-contexts.md`](../vision/bounded-contexts.md) (§Operations, §Integration Topology) · [`../vision/domain-events.md`](../vision/domain-events.md) (§Operations) · [`../narratives/008-operator-resolves-dispute-with-extension.md`](../narratives/008-operator-resolves-dispute-with-extension.md) (first operator-perspective narrative; the operator-vantage spec for the dispute/escalation queue) · [`../skills/marten-projections/SKILL.md`](../skills/marten-projections/SKILL.md) · [`../skills/wolverine-message-handlers/SKILL.md`](../skills/wolverine-message-handlers/SKILL.md) · [`../skills/integration-messaging/SKILL.md`](../skills/integration-messaging/SKILL.md) · [`../skills/critter-stack-testing-patterns/SKILL.md`](../skills/critter-stack-testing-patterns/SKILL.md) · [`../decisions/014-cross-bc-read-model-extension-shape.md`](../decisions/014-cross-bc-read-model-extension-shape.md) · [`../decisions/021-openspec-cli-for-m6.md`](../decisions/021-openspec-cli-for-m6.md) · [`../decisions/README.md`](../decisions/README.md)

---

## 1. Goal & Exit Criteria

### Goal

Deliver the **Operations BC** — the internal staff view and the projector-facing dashboard backend for live demonstrations. Operations is a **pure consumer**: it subscribes to integration events from every other BC and folds them into denormalized operator read models. It originates no domain events and publishes no integration events. Its outputs are (a) queryable read models for the staff dashboard and (b) the `[Authorize]`-gated HTTP query endpoints that surface them.

M7 also carries the project's **authentication-posture resumption**. `CLAUDE.md` pinned `[AllowAnonymous]` "through M6"; M7 is where real authentication planning resumes. This is a milestone-level convention change — flagged as the headline M7-S1 foundation decision and recorded as an ADR (next unreserved: `024-<slug>.md`), not a silent flip. The MVP target is a config-driven staff passphrase guarding a `StaffOnly` policy on staff surfaces; full staff identity remains post-MVP.

At M7 close, Relay's already-wired `OperationsHub` (M6-S6) has the read models behind it: the operator dashboard's data exists and is queryable, the dispute/escalation queue narrative 008 dramatises is backed by a real `OperationsObligationsView`, and the staff surfaces are auth-gated. The React ops dashboard SPA that renders all this is **M8** — M7 ships the backend it consumes.

### Exit criteria

- [ ] Solution builds clean with `dotnet build` — 0 errors, 0 warnings (engine baseline: Wolverine 6 / Marten 9 / JasperFx 2)
- [ ] Operations BC implemented: `CritterBids.Operations` and `CritterBids.Operations.Tests` projects, `AddOperationsModule()`, Marten config per BC-module conventions and ADR 011 (no direct `AddMarten()` — types contributed via `services.ConfigureMarten()`)
- [ ] `AddOperationsModule()` called in `Program.cs`; Operations discovery added to `opts.Discovery.IncludeAssembly(...)`
- [ ] Authentication-posture resumption decided in M7-S1 and recorded as an ADR (scheme, `StaffOnly` policy, which endpoints change, SignalR hub auth, test strategy)
- [ ] OpenSpec adoption decision for Operations recorded in the M7-S1 prompt/retro and the `openspec/README.md` adoption ledger (adopt / decline / defer per ADR 021)
- [ ] Cross-BC consumer routes wired in `Program.cs`: `operations-settlement-events` gains its `ListenToRabbitQueue()` (pre-wired publish-only since M5-S6, carrying `PaymentFailed`); all other `operations-*` consumer queues and their publish-route additions are added (routing-only — no upstream BC code change)
- [ ] Operator read models implemented in the `operations` Marten schema: settlement queue, lot board / bid-activity feed, `OperationsObligationsView` (escalation queue + open-dispute queue per narrative 008), session & participant activity board
- [ ] Staff query endpoints implemented for the read models, gated by the `StaffOnly` authorization policy
- [ ] The existing staff-mutation endpoints in their owning BCs (`ResolveDisputeEndpoint` in Obligations, `WithdrawListing` in Selling, `CreateSession`/`StartSession` in Auctions) gain the `StaffOnly` policy — they are **not** re-homed into Operations
- [ ] At least one authorized-vs-unauthorized test per staff surface (401/403 on missing/invalid staff credential; 200 with it)
- [ ] At least one read-model projection test per operator view exercising the consume → upsert/append path against a real Postgres (Testcontainers)
- [ ] Full solution layout updated; `bounded-contexts.md` Operations status flipped from "Planned" to shipped at M7 close
- [ ] M7-S1 through final-session retrospective docs written
- [ ] M7 retrospective doc written

---

## 2. In Scope

### Operations BC — core components

| Component | What it owns | Design source |
|---|---|---|
| Cross-BC consumer handlers | One Wolverine handler (or sibling-handler family per source BC) per consumed integration-event type; each folds the event into the relevant operator read model | `bounded-contexts.md` §Operations; ADR 014 Path A |
| Settlement queue view | Operator view of in-flight and failed settlements; fed by Settlement events (incl. `PaymentFailed` flagged for staff attention) | `bounded-contexts.md`; `domain-events.md` `PaymentFailed` |
| Lot board / bid-activity feed | Live lot board (current high bid, status per listing) plus an append-style bid-activity feed | `bounded-contexts.md` §Operations |
| `OperationsObligationsView` | Escalation queue + open-dispute queue; fed by `DeadlineEscalated`, `DisputeOpened`, `DisputeResolved`; the surface narrative 008's operator works from | narrative 008 (operator-vantage spec); `domain-events.md` §Obligations |
| Session & participant activity board | Session lineup + participant-session activity; fed by `SessionCreated`, `SessionStarted`, `ListingAttachedToSession`, `ParticipantSessionStarted` | `bounded-contexts.md`; `domain-events.md` |
| Staff query endpoints | `[Authorize]`-gated (`StaffOnly`) HTTP query endpoints surfacing the read models for the dashboard | This milestone (auth resumption) |

### Auth posture resumption

M7 is the **auth milestone**. The MVP target is the config-driven staff passphrase named in `bounded-contexts.md` §Operations ("Staff authentication seam — config-driven passphrase in MVP, extensible to full staff identity"). M7-S1 decides the concrete shape and records it as an ADR. The decision is broader than "add `[Authorize]`": `Program.cs` currently calls bare `AddAuthentication()` / `AddAuthorization()` with no default scheme, so a naive attribute flip would fail at runtime. The ADR must settle:

- The authentication scheme shape (e.g. a config-passphrase header / demo token vs cookie)
- The authorization policy name (working name: `StaffOnly`) and what claim/credential satisfies it
- Which existing endpoints change in M7 (staff mutation endpoints listed below) and which participant-facing endpoints remain `[AllowAnonymous]` for MVP
- SignalR `OperationsHub` authentication and staff-group membership
- The test strategy for authorized/unauthorized Wolverine HTTP endpoints and the hub

### Staff-command ownership boundary

**Operations owns staff-facing read models and query/UX seams. The owning domain BC owns the mutation and emits its own events.** Operations must not directly produce another BC's events (`ListingWithdrawn`, `SessionCreated`/`SessionStarted`, `DisputeResolved`) — doing so would either require a forbidden BC-to-BC project reference or contradict Operations' "publishes no integration events" stance.

The staff mutations already exist as endpoints in their owning BCs:

| Staff action | Existing endpoint / command | Owning BC | M7 work |
|---|---|---|---|
| Resolve a dispute | `ResolveDisputeEndpoint` | Obligations (M6) | Apply `StaffOnly` policy |
| Force-withdraw a listing | `WithdrawListing` | Selling (M4-S2) | Apply `StaffOnly` policy |
| Create / start a Flash Session | `CreateSession` / `StartSession` | Auctions (M4-S5) | Apply `StaffOnly` policy |

M7's command-side work is therefore **auth-gating existing endpoints**, not building new mutation handlers in Operations. Whether the dashboard also gets a thin Operations-side query/launch convenience seam is an M7-S1 scope call; the default is to call the owning BC's endpoint directly.

### Cross-BC consumer wiring (enumerated)

"All significant events → Operations" in `bounded-contexts.md` is the target topology; M7 enumerates the **required** subset that feeds the named views and defers the remainder. New `operations-*` queues each get their own `ListenToRabbitQueue()` plus the publish-route additions that feed them (routing-only `Program.cs` edits — no upstream BC code change, consistent with the M6 Relay route pattern):

| Queue | Status entering M7 | Events (M7-required) | Feeds |
|---|---|---|---|
| `operations-settlement-events` | Pre-wired publish-only since M5-S6 (carries `PaymentFailed`) | `PaymentFailed`, `SettlementCompleted`, `SellerPayoutIssued` | Settlement queue |
| `operations-obligations-events` | **Not yet wired** (Obligations events currently route to `relay-obligations-events` only) | `DeadlineEscalated`, `DisputeOpened`, `DisputeResolved`, `ObligationFulfilled` | `OperationsObligationsView` |
| `operations-auctions-events` | Not yet wired | `BiddingOpened`, `BidPlaced`, `ListingSold`, `ListingPassed`, `ListingWithdrawn`, `SessionCreated`, `SessionStarted`, `ListingAttachedToSession` | Lot board / bid-activity feed; session board |
| `operations-selling-events` | Not yet wired | `ListingPublished` | Lot board (listing rows appear at publish) |
| `operations-participants-events` | Not yet wired | `ParticipantSessionStarted` | Participant activity board |

The exact event-per-view assignment is refined in the per-slice prompts; the enumeration above is the M7 scope ceiling. Events outside this list (the long tail of "all significant events") are **deferred** — slice authors treat the enumerated set as closed and escalate additions rather than expanding silently.

---

## 3. Explicit Non-Goals

- **React ops dashboard SPA.** The staff dashboard UI (lot board, dispute-resolution controls, settlement queue, the projector-legible live view) is **M8 (frontend)**. M7 ships the queryable backend and the `OperationsHub` data behind it, testable via integration tests and a SignalR test client. No browser UI.
- **`OperationsHub` does not move to Operations.** The hub stays in Relay (per the M6 retro). Operations builds the read-model projections and the staff query endpoints; live push continues to originate from Relay's `OperationsHub`. The only M7 hub work is the optional staff-group targeting refinement (below), which is a Relay edit.
- **Full staff identity / user management.** MVP auth is a config-driven staff passphrase gating a `StaffOnly` policy. Per-user staff accounts, roles beyond staff/anonymous, password reset, and an identity provider are post-MVP.
- **Flipping participant-facing endpoints off `[AllowAnonymous]`.** M7 introduces auth on **staff surfaces**. Whether (and when) participant endpoints gain real auth is decided by the M7-S1 ADR; the default is that participant endpoints remain anonymous for MVP and only staff surfaces are gated.
- **`DemoResetInitiated` cascade.** The graceful demo-reset command that cascades through BCs is `domain-events.md`-flagged post-MVP. MVP demo reset remains Docker-volume removal. No reset command ships in M7.
- **Operations as an event store of inbound events.** Operations builds read models via handler-driven upsert/append (ADR 014 Path A), not by persisting every inbound integration event into local Marten streams for replay. Local event-sourcing of the firehose is out of scope.
- **Compensation / settlement reversal on dispute resolution.** Unchanged from M6 — `DisputeResolved` carries a `ResolutionType` but no reversal logic ships.
- **Upstream BC behavior changes.** Operations consumes existing events. Any event it needs is either already in `CritterBids.Contracts` or reached via a publish-route addition in `Program.cs` only — no Auctions / Selling / Settlement / Obligations / Participants BC code changes.

---

## 4. Solution Layout

### New projects added in M7

- `src/CritterBids.Operations/` — Operations BC implementation (consumer handlers, read-model documents, staff query endpoints, module wiring)
- `tests/CritterBids.Operations.Tests/` — Operations BC test project; xUnit + Shouldly + Testcontainers + Alba

### New files added in M7 (representative, not exhaustive)

Operations BC:

- `OperationsModule.cs` — DI wiring; `AddOperationsModule()` extension; Marten registration via `services.ConfigureMarten()`
- `SettlementQueueView.cs`, `LotBoardView.cs`, `BidActivityFeed*.cs`, `OperationsObligationsView.cs`, `SessionActivityView.cs` — read-model documents (upsert views + append/feed views distinguished per §6)
- Per-source-BC consumer handlers (sibling-handler families per ADR 014 Path A): e.g. `SettlementQueueHandlers.cs`, `LotBoardHandlers.cs`, `ObligationsQueueHandlers.cs`, `SessionActivityHandlers.cs`
- Staff query endpoints (`[Authorize]`/`StaffOnly`): e.g. `OperationsDashboardEndpoints.cs`
- Auth wiring: a `StaffOnly` policy + the config-passphrase scheme (location per M7-S1 ADR — `Program.cs` and/or an `Operations`-internal auth extension)

Contracts:

- No new `CritterBids.Contracts.Operations` namespace is expected — Operations is a pure consumer and publishes nothing.

API host wiring:

- `src/CritterBids.Api/Program.cs` — `builder.Services.AddOperationsModule()`; `opts.Discovery.IncludeAssembly()` for Operations; the `operations-*` consumer routes and publish-route additions; the auth scheme + `StaffOnly` policy registration

### Full solution layout at M7 close

```
src/
├── CritterBids.Api/
├── CritterBids.AppHost/
├── CritterBids.Contracts/
│   ├── Auctions/        (M3 / M4)
│   ├── Obligations/     (M6)
│   ├── Participants/    (M5-S5 promotion)
│   ├── Selling/         (M2 / M4-S2)
│   └── Settlement/      (M5)
├── CritterBids.Auctions/      (M3 / M4)
├── CritterBids.Listings/      (M2 / M3-S6)
├── CritterBids.Obligations/   (M6)
├── CritterBids.Operations/    ← NEW IN M7
├── CritterBids.Participants/  (M1)
├── CritterBids.Relay/         (M6)
├── CritterBids.Selling/       (M2 / M4-S2)
└── CritterBids.Settlement/    (M5)

tests/
├── CritterBids.Api.Tests/
├── CritterBids.Auctions.Tests/
├── CritterBids.Listings.Tests/
├── CritterBids.Obligations.Tests/
├── CritterBids.Operations.Tests/  ← NEW IN M7
├── CritterBids.Participants.Tests/
├── CritterBids.Relay.Tests/
├── CritterBids.Selling.Tests/
└── CritterBids.Settlement.Tests/
```

At M7 close all eight MVP BCs exist in `src/`. The remaining MVP work is the frontend (M8).

---

## 5. Infrastructure

### Marten configuration

Operations uses Marten on PostgreSQL per ADR 011. It does not call `AddMarten()` directly — it contributes its read-model document types via `services.ConfigureMarten()` inside `AddOperationsModule()`. Read models live in a dedicated `operations` schema. Operations registers no sagas and no event-sourced aggregates; it owns documents only (upsert views and append/feed rows).

### RabbitMQ routing summary

New routes added in M7 (`Program.cs` additions) — see §2 for the per-view event assignment:

| Queue | Direction | Notes |
|---|---|---|
| `operations-settlement-events` | In (Operations listens) | Publish-side pre-wired for `PaymentFailed` since M5-S6; M7 adds the `ListenToRabbitQueue()` and the additional `SettlementCompleted` / `SellerPayoutIssued` publish routes the settlement queue needs |
| `operations-obligations-events` | In (Operations listens); Out (publish-route additions) | New queue. Obligations events currently route only to `relay-obligations-events`; M7 adds the `operations-obligations-events` publish routes (`DeadlineEscalated`, `DisputeOpened`, `DisputeResolved`, `ObligationFulfilled`) and the `ListenTo` |
| `operations-auctions-events` | In (Operations listens); Out (publish-route additions) | New queue; publish-route additions for the enumerated Auctions event set |
| `operations-selling-events` | In (Operations listens); Out (publish-route additions) | New queue; `ListingPublished` |
| `operations-participants-events` | In (Operations listens); Out (publish-route additions) | New queue; `ParticipantSessionStarted` |

`AutoProvision()` declares the new queues at startup. Publish-side additions for already-published events going to new `operations-*` queues are additions to existing message types' routing, not new contract types. Existing queues stay unchanged.

### Read-Relay eventual-consistency note

Two independent consumers react to the same upstream event: Relay's `OperationsHub` pushes a live signal, and Operations folds the event into a read model on its own queue. These are not ordered with respect to each other. **The Relay push is a live-feed / cache-invalidation signal, not proof that the Operations read model already reflects the change.** The dashboard (M8) must tolerate this — refetch with short backoff, or treat the push as "something changed, re-query" — and the Operations query endpoints provide no read-your-own-write guarantee relative to a Relay push. M7 documents this; it does not attempt to order the two consumers.

### Testing infrastructure

Per the M6 testing lessons: Operations read-model and query-endpoint tests run against a real Postgres via **Testcontainers**. Any test that asserts `OperationsHub` push behavior (the optional staff-group targeting refinement) needs a **real-Kestrel** host — Alba's in-memory `TestServer` cannot drive a SignalR `HubConnection`. Operations consumer handlers that share an event type with another BC's handler (e.g. `ListingSold`, consumed by Listings, Settlement, Relay, and Operations) apply the `*BcDiscoveryExclusion` isolation pattern from `critter-stack-testing-patterns` and `DisableAllExternalWolverineTransports()` in fixtures, per the M5 `tracked.Sent` vs `tracked.NoRoutes` learning.

### No new stores

Operations uses the same Marten-on-PostgreSQL store as every other BC. No new database, container, or transport. (The all-Marten pivot, ADR 011, originally listed Operations among the BCs migrated off Polecat; M7 is the first time Operations is actually built, and it is Marten from birth.)

---

## 6. Conventions Pinned

### Auth posture resumption (S1 decision — ADR-worthy)

M7-S1 makes the authentication decision and records it as an ADR (next unreserved `024-<slug>.md`). The decision covers scheme, `StaffOnly` policy, the endpoint set that changes, hub auth, and test strategy (see §2 "Auth posture resumption"). MVP scope is a config-driven staff passphrase; full identity is post-MVP. This is the one genuinely new architectural surface in M7 and the highest-risk part of the milestone — it is a foundation decision, not an implementation detail discovered mid-slice.

### Read-model build strategy — ADR 014 Path A

Operations' cross-BC views follow **ADR 014 Path A**: one view per logical operator surface, one sibling handler (or handler family) per source BC/event family, tolerant upsert, status-preservation guards. This is the same pattern the Listings catalog uses for its cross-BC `CatalogListingView`. Operations does **not** use Marten multi-stream event projections — those require the inbound events to be appended into local Operations streams first, which is explicitly out of scope (§3). Two view shapes are distinguished:

- **Upsert views** — lot board, settlement queue, `OperationsObligationsView`, session/participant board. One row per logical entity, updated in place.
- **Append / feed views** — bid-activity feed, operator audit feed. One row per event (or a capped list), not upsert-in-place. These need an append shape rather than an upsert projection.

### Operations is a pure consumer

Operations publishes no integration events and originates no domain events. Consumer handlers return `void` / `Task` (read-model writes happen via the injected Marten session inside the handler, or via Wolverine's declarative persistence side effects) — they do **not** return `OutgoingMessages`, and they make no `IMessageBus` calls. `bounded-contexts.md` §Operations "Integration out: None" is enforced structurally.

### Staff-command ownership boundary

Mutations stay in their owning BC (see §2). Operations does not re-home `ResolveDispute`, `WithdrawListing`, or the Session commands, and never directly emits another BC's events. M7's command-side work is applying the `StaffOnly` policy to those existing endpoints.

### `OperationsHub` ownership stays in Relay

Per ADR 023 and the M6 retro, the SignalR hubs live in Relay. Operations builds the read models and query endpoints behind the staff feed. The only M7 hub edit is the optional `OperationsHub` staff-group targeting refinement (M6-S6 standardized to `Clients.All`); whether that lands in M7 or defers is an M7-S1 scope call, and if it lands it is a Relay change, not an Operations one.

### OpenSpec adoption (S1 decision)

Operations evaluates OpenSpec adoption at its opening session per ADR 021 (the capability working name is `operator-dashboards`). The three valid outcomes are adopt (Obligations path), decline (Relay path; proceed under ADR 020 alone), or defer. The decision is recorded in the M7-S1 prompt and the `openspec/README.md` adoption + capability ledgers. (Relay's decline row already landed at M6 closeout — no Relay housekeeping is owed here.)

### `[AllowAnonymous]` → `StaffOnly` transition is explicit

Every endpoint added or modified in M7 carries an explicit authorization attribute — `StaffOnly` for staff surfaces, `[AllowAnonymous]` retained deliberately for any participant-facing surface kept open for MVP. No endpoint is left implicitly unprotected; the absence of an attribute is treated as a bug once the scheme exists.

---

## 7. Slice Breakdown

M7 ships in seven slices. The first is a foundation slice (no BC project), the next four build the BC and its read models, the sixth lands the auth gating across staff surfaces, and the last closes the milestone end-to-end.

| Slice | Title | Scope |
|---|---|---|
| M7-S1 | Foundation Decisions — Auth Posture + OpenSpec Adoption + Read-Model Strategy + Source Audit | Authentication ADR-024 (scheme, `StaffOnly`, endpoint set, hub auth, test strategy); OpenSpec adopt/decline/defer for Operations (`operator-dashboards`); confirm ADR 014 Path A read-model strategy; a lightweight Operations source-audit / mini event-modeling pass to freeze the read-model field set (no Operations workshop exists, only narrative 008); staff-command ownership rule pinned. No `CritterBids.Operations` project created |
| M7-S2 | Operations BC Scaffold + First Consumer | `CritterBids.Operations` + `CritterBids.Operations.Tests`; `AddOperationsModule()`; Marten config (`operations` schema); `Program.cs` wiring + discovery; `operations-settlement-events` `ListenTo`; first read model (settlement queue, incl. `PaymentFailed` flagging) seeded end-to-end |
| M7-S3 | Lot Board + Bid-Activity Feed | `operations-auctions-events` + `operations-selling-events` consumers; lot board upsert view (listing rows from `ListingPublished`, status from the Auctions outcome events); bid-activity append feed |
| M7-S4 | `OperationsObligationsView` — Escalation + Dispute Queues | `operations-obligations-events` publish routes + `ListenTo`; `OperationsObligationsView` (escalation queue from `DeadlineEscalated`; open-dispute queue from `DisputeOpened`, cleared by `DisputeResolved`; cleared from active set by `ObligationFulfilled`) — the narrative 008 operator surface |
| M7-S5 | Session & Participant Activity Board | `operations-participants-events` consumer (+ the session events from `operations-auctions-events`); session lineup + participant-session activity board |
| M7-S6 | Staff Auth Gating + Query Endpoints | Implement the M7-S1 auth scheme + `StaffOnly` policy; staff query endpoints over all read models; apply `StaffOnly` to the existing staff-mutation endpoints (`ResolveDisputeEndpoint`, `WithdrawListing`, `CreateSession`/`StartSession`); authorized-vs-unauthorized tests; optional `OperationsHub` staff-group targeting refinement (Relay) if M7-S1 scoped it in |
| M7-S7 | End-to-End Integration + Housekeeping | Cross-BC journey test producing real activity into every operator view; `Program.cs` route audit; `bounded-contexts.md` Operations status flip; the owed `wolverine-signalr` skill lived-Relay update folded in or confirmed; test-count baseline updated; M7 retrospective |

---

## Document History

- **v0.1** (2026-05-30): Authored as the M7 opening artifact, immediately after the M6 milestone closed. Scope derived from the M6 retrospective's "What's Next — M7" / "What M7 Should Know" sections, `bounded-contexts.md` §Operations, `domain-events.md` §Operations, and narrative 008 (the operator-vantage spec). Scope reviewed against the M5/M6 seven-slice cadence: staff mutations were pinned to their owning BCs (boundary-safety) rather than re-homed into Operations, the "all significant events → Operations" topology was narrowed to an enumerated M7-required event set, and the auth-posture resumption was framed as the headline S1 foundation decision (ADR-024) rather than pre-decided here. Status `Planned`; M7-S1 not yet started.
