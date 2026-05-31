# M7-S1: Operations Foundation Decisions — Retrospective

**Date:** 2026-05-30
**Milestone:** M7 — Operations BC
**Slice:** S1 — Foundation Decisions (Auth Posture ADR-024 + OpenSpec Adoption Call + Read-Model Field Freeze)
**Agent:** @PSA (with @PO consulted on all five open-question forks)
**Prompt:** `docs/prompts/implementations/M7-S1-operations-foundation-decisions.md`

## Baseline

- Branch `erikshafer/m7-s1-operations-foundation-decisions` off main @ `082730e` (PR #63 — "Add M7-S1 Operations foundation-decisions session prompt").
- Design inputs already landed: M7 milestone doc, narrative 008 (`accepted`, v1.0), ADR 014 (Path A), ADR 023 (Relay broadcast), ADR 020/021 (spec-delta loop + OpenSpec adoption).
- `docs/decisions/README.md` next-unreserved ADR was `024`. Workshops index next number was `W006`.
- `openspec/README.md` ledgers: Operations capability `operator-dashboards` = `proposed (working)`; Operations adoption = `⏸ evaluate at opening`. Relay already `❌ decline`.
- `src/CritterBids.Api/Program.cs` auth state: bare `AddAuthentication()` + `AddAuthorization()` with **no default scheme**; `UseAuthentication()` / `UseAuthorization()` already in the pipeline. No `[Authorize]` anywhere — every surface is uniformly anonymous (the `[AllowAnonymous]`-through-M6 stance).
- No `CritterBids.Operations` project exists (planned, not yet scaffolded).

## Items completed

| Item | Description |
|------|-------------|
| S1a | ADR-024 — staff auth-posture resumption (`StaffToken` scheme + `StaffOnly` policy + endpoint set + hub auth + test strategy); ADR index + next-number pointer (→025) updated |
| S1b | OpenSpec adopt/decline/defer decision for Operations: **decline**; `openspec/README.md` ledgers flipped |
| S1c | Read-model field-freeze source audit — `docs/workshops/006-operations-source-audit.md`; workshops index + next-number pointer (→W007) updated |
| S1d | This retrospective |

## The five forks (decided in-session with @PO)

All five open questions were genuine forks taken to @PO, each with a recommendation; @PO accepted all five as recommended:

1. **Auth-scheme transport** → custom config-bound `StaffToken` scheme (header `X-Staff-Token` for HTTP; `access_token` query string for `/hub/operations`), registered as the default authenticate + challenge scheme. Rejected cookie (no staff browser-login flow) and bearer/JWT (over-builds a token-issuance path for one shared secret).
2. **OpenSpec for Operations** → **decline**, proceed under ADR 020 alone. Operations is a pure-consumer read-model BC with no saga and no published contracts — a lighter SHALL-surface than Obligations — and Relay set the decline precedent. Recorded in the ledgers; no `openspec/changes/<slug>/` folder created.
3. **Field-freeze home** → new `docs/workshops/006-operations-source-audit.md`. It is where the S2–S5 prompts look first (the Operations design home), and the workshop number was free.
4. **`OperationsHub` staff-group targeting** → **deferred** past M7. Once the hub is `StaffOnly`-gated every connected client is staff, so `Clients.All` is an all-staff broadcast for MVP; group segmentation is a post-MVP Relay edit.
5. **Thin Operations-side query/launch seam** → **no seam**. The dashboard calls the owning BCs' endpoints directly; introducing an Operations-side passthrough would re-home staff mutations and blur the BC boundary for no MVP payoff.

## S1a: ADR-024 — staff auth-posture resumption

**Why this approach.** The crux is a runtime trap, not a style choice: the host runs
`AddAuthentication()` with **no default scheme**, so the naive M7 move — flip `[Authorize]` onto the
staff surfaces — throws `InvalidOperationException` ("No authenticationScheme was specified, and
there was no DefaultChallengeScheme found") at request time and the endpoint **500s instead of
401s**. ADR-024 fixes the cause: register one custom `StaffToken` scheme as the **default**
authenticate + challenge scheme. A single shared config passphrase is the MVP credential; the scheme
issues an authenticated principal carrying a fixed `staff` claim (`RequireClaim("staff","true")`) on a
match. Cookie and bearer/JWT were rejected as over-build for one shared secret (recorded in the ADR's
Options).

**The endpoint-set correction.** The milestone §2 frames all four staff mutations as "already exist
as endpoints." A code check found that is true for **only one**: `ResolveDisputeEndpoint`
(`[WolverinePost("/api/obligations/disputes/resolve")]`, 202). `WithdrawListing`, `CreateSession`,
and `StartSession` are **command-only handlers with no `[WolverinePost]`** ("no HTTP endpoint in M4,
dispatch-only" / "drop-in for the HTTP wiring later"). ADR-024 splits the decision accordingly: apply
the policy to the one existing endpoint; for the three command-only handlers, S6 **wires the
owning-BC HTTP endpoint first, then gates it**. This is a finding against the milestone's framing (see
below), surfaced by a rubber-duck critique and confirmed in code.

**Other decisions pinned:** non-empty staff token required (empty never authenticates; S6 fails
startup if gated surfaces are enabled with no token); `access_token` query read is explicit in the
custom handler (not framework-automatic — only `AddJwtBearer` ships that plumbing) and scoped to
`/hub/operations`, HTTPS-only, redacted from logs; 401 (no/invalid token) vs 403 (authenticated,
missing claim — post-MVP headroom); test strategy is real Kestrel + Testcontainers, with the SignalR
`HubConnection` the binding reason a real socket is needed. ADR-024 also records that it **supersedes
CLAUDE.md's `[AllowAnonymous]`-through-M6 pin** — the CLAUDE.md text edit and the endpoint code land
in **S6**, not here (mirrors M6-S1 deferring `ObligationsOptions` code to its project's slice).

**Rubber-duck pass.** The ADR draft was critiqued before finalizing. One blocking finding (the
endpoint-existence gap above) and six non-blocking findings were adopted: explicit `access_token`
read, success-status wording (202 not 200), empty-token startup validation, query-string log-leak
mitigations, the exact `staff` claim contract, and the hub-is-the-Kestrel-reason clarification.

## S1b: OpenSpec decision — decline

Operations declines OpenSpec and proceeds under ADR 020's spec-delta closure loop alone. The case:
Operations publishes **no** integration events and owns **no** saga — its entire spec surface is
"project these upstream events into these read-model fields," which the field-freeze artifact (S1c)
captures completely in prose. The heavier OpenSpec change/spec/tasks ceremony buys nothing for a
pure-consumer BC, and Relay already set the decline precedent at M6 closeout. Both `openspec/README.md`
ledgers were flipped (capability `proposed (working)` → `confirmed (not adopted)`; adoption
`⏸ evaluate at opening` → `❌ decline`, dated 2026-05-30), and the orientation bullet that said Relay
and Operations "evaluate at their openings" was updated to record both as resolved. **No
`openspec/changes/` folder was created** — declining means the directory stays irrelevant to
Operations.

## S1c: Read-model field freeze

`docs/workshops/006-operations-source-audit.md` freezes the field shape of all five M7 operator read
models, each field traced to a real `CritterBids.Contracts` payload (16 event records audited against
source). Classifications: **upsert** — settlement queue (`SettlementId`), lot board (`ListingId`),
`OperationsObligationsView` (`ObligationId`), and the session/participant board (split per ADR 014's
one-view-per-entity rule into a `SessionId`-keyed and a `ParticipantId`-keyed view); **append/feed** —
the bid-activity feed (`BidId`, one immutable row per `BidPlaced`). ADR 014 Path A is **confirmed**
(not re-decided) as the build strategy, with the Operations-specific consequence recorded: no Marten
multi-stream projections, because the inbound firehose is not appended to local Operations streams
(milestone §3 non-goal) — handlers upsert documents directly off the bus. *Derived* fields (status
from event type) and *cross-view* fields (joined from a sibling view at render time) are marked
distinctly from payload-traced fields. The artifact is a single source-audit document, a deliberate
deviation from the paired scenarios + deep-dive workshop convention (there are no GWT scenarios to
write); the workshops index records the deviation.

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Session close | none run | N/A — docs-only slice |

No build and no test run. This slice touches **zero** code: one new ADR, one new workshop artifact,
one new retro, and three index/ledger edits. Per the prompt, no `dotnet build` / `dotnet test` is
required for a docs-only decisions slice. The test *strategy* this slice writes (ADR-024 §Test
strategy) is implemented in S6.

## Build state at session close

- `CritterBids.Operations` project: does not exist (S2+).
- `Program.cs`: unchanged. `AddAuthentication()` still has no default scheme — ADR-024 records that S6
  registers `StaffToken`; no scheme is registered this slice.
- Handlers / read-model classes / endpoints added: 0.
- New `CritterBids.Contracts.*` types: 0 (Operations is a pure consumer — the freeze adds no contract).
- Files changed: 2 new docs (`024-…`, `006-…`), 1 new retro, 3 index/ledger edits (`docs/decisions/README.md`, `docs/workshops/README.md`, `openspec/README.md`).

## Key learnings

1. **The milestone doc's "already exist as endpoints" was true for one of four staff mutations.** A
   code check beat the doc: only `ResolveDisputeEndpoint` is an HTTP endpoint; the other three are
   dispatch-only command handlers. The lesson reaffirms the project's "code is the source of truth"
   discipline — a decisions slice that pins an endpoint set must verify the endpoints exist before
   pinning, not trust the milestone's forward-looking prose. ADR-024 records the split so S6 isn't
   surprised; the milestone framing is flagged as a finding below.
2. **A no-default-scheme `AddAuthentication()` is a 500, not a 401.** The auth-resumption decision is
   really about avoiding a runtime trap, not picking a credential format. Registering a single custom
   scheme as the default is the minimal correct fix; the credential format (shared passphrase) is the
   easy part.
3. **Pure-consumer BCs are the natural place to decline OpenSpec.** A BC with no published contracts
   and no saga has its entire spec captured by a field-freeze artifact; the change/spec/tasks ceremony
   adds process without adding a SHALL surface. Decline is the right default for read-model-only BCs.
4. **A field freeze surfaces gaps the narrative hid.** Tracing every `OperationsObligationsView` field
   to a payload exposed that narrative 008's open-dispute card wants a listing `Title` and a "winner"
   that the obligations events do not carry — caught now, in prose, instead of in S4 code.

## Findings against narrative

Narrative 008 (`docs/narratives/008-operator-resolves-dispute-with-extension.md`) is the slice's
anchor and the operator-vantage spec for `OperationsObligationsView`. This is a foundation slice — it
implements none of the narrative's Moments (the view is built in S4). Two findings surfaced, both
routed to the field-freeze artifact rather than to code:

- **`workshop-update` (field freeze):** the narrative's open-dispute card text ("NonDelivery — boxed
  vintage synthesizer — raised by buyer") implies a listing **`Title`** and a **winner** on the view.
  Neither is on any Obligations event: `Title` is *cross-view* (joined from the lot board by
  `ListingId`), and the "winner" on the open-dispute card is `DisputeOpened.RaisedBy`, not `WinnerId`
  (which arrives only with `ObligationFulfilled`). Recorded in W006 view 4 so S4 reads the sibling view
  and surfaces `RaisedBy` rather than waiting on a structurally-absent field — and crucially does **not**
  mint a contract type to carry them (Operations stays a pure consumer).
- **`milestone-note` (endpoint framing):** milestone §2 calls all four staff mutations "existing
  endpoints"; three are command-only. Captured in ADR-024 §Decision item 3. No milestone-doc edit this
  slice — the milestone remains scope-authoritative; the framing is corrected in the ADR S6 builds from.

Narrative 008's `## Document History` gains **no** row this slice — no Moment was implemented.

## Spec delta — landed?

Landed as written. Per ADR 020, this slice's spec consequence is the authoring of **ADR-024** (a new
governing decision — status ✅ Accepted, indexed in `docs/decisions/README.md`, next-unreserved
advanced to `025`) plus the **read-model field-freeze artifact** at its recorded home
(`docs/workshops/006-operations-source-audit.md`, workshops index advanced to `W007`). The **OpenSpec
adopt/decline/defer decision** — itself part of this slice's spec consequence — resolved to **decline**
and is recorded in both `openspec/README.md` ledgers; no `openspec/changes/` folder exists, which is
the correct physical consequence of declining. No read-model behavior, handler, saga, endpoint, or
`Program.cs` change was made — those remain unimplemented until S2+, as planned. No narrative or
workshop `## Document History` row was required (no Moment implemented; W006 is newly authored, not an
amendment to an existing workshop).

## Verification checklist

- [x] `docs/decisions/024-staff-auth-posture-resumption.md` exists, status ✅ Accepted; README index row added + next-number pointer advanced to `025`
- [x] OpenSpec decision recorded in `openspec/README.md` ledgers (capability `confirmed (not adopted)`; adoption `❌ decline`, 2026-05-30); **no** `openspec/changes/` folder created
- [x] Field-freeze artifact at `docs/workshops/006-operations-source-audit.md` covers all five operator read models, each classified upsert vs append/feed, each field traced to an existing `CritterBids.Contracts` payload, with ADR 014 Path A confirmed; workshops index updated (→W007)
- [x] No `CritterBids.Operations` / `CritterBids.Operations.Tests` project; no handler, read-model, or endpoint code; no `Program.cs` change
- [x] No new `CritterBids.Contracts.*` type (Operations is a pure consumer)
- [x] Docs-only — no `dotnet build` / `dotnet test` required; none run
- [x] This retrospective written with `**Prompt:**` header and `## Spec delta — landed?`
- [x] One PR off main; no commit to `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M7-S2 (next):** scaffold `CritterBids.Operations` + `.Tests`; `AddOperationsModule()`; Marten
  config; `Program.cs` wiring; begin the Path A read models the freeze locked (settlement queue, lot
  board, bid-activity feed per the slice map).
- **M7-S6 (auth application):** register the `StaffToken` scheme + `StaffOnly` policy in `Program.cs`;
  apply `[Authorize(StaffOnly)]` to `ResolveDisputeEndpoint` and `OperationsHub`; **wire the
  owning-BC HTTP endpoints** for `WithdrawListing` / `CreateSession` / `StartSession`, then gate them;
  add explicit `[AllowAnonymous]` to participant surfaces; **edit CLAUDE.md** to retire the
  `[AllowAnonymous]`-through-M6 pin and point at ADR-024; implement the real-Kestrel + Testcontainers
  auth tests.
- **S4 (`OperationsObligationsView`):** honour the field-freeze cross-view findings — join the lot
  board for `Title`, surface `RaisedBy` (not `WinnerId`) on the open-dispute card.
