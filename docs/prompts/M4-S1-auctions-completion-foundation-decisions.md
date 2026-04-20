# M4-S1: Auctions Completion Foundation Decisions + Contract Stubs

**Milestone:** M4 — Auctions BC Completion
**Session:** S1 of 7 (plus pre-drafted S4b and S5b split slots)
**Prompt file:** `docs/prompts/M4-S1-auctions-completion-foundation-decisions.md`
**Baseline:** 86 tests passing · `dotnet build` 0 errors, 0 warnings · M3 complete

---

## Goal

Close the four open decisions that block M4 implementation and lock the remaining Auctions + Selling
integration vocabulary before any M4 handler code is written. S1 is docs-only except for six new
`sealed record` contract stubs in `src/CritterBids.Contracts/Auctions/`, the extension of the existing
`Selling/ListingWithdrawn.cs` stub to its full M4 payload, and a new `AuctionsIdentityNamespaces.cs`
constant file that pins M4-D1's composite-key shape in code — each of these is a vocabulary-lock
artifact whose shape is the output of the decisions being made, not implementation.

M4 lands the second saga (first with a composite correlation key), the Session aggregate (first
non-`Listing` aggregate in Auctions), and the real Selling-side `WithdrawListing` producer. Starting
S2 with ADR 007 Gate 4 still pending, an unpinned composite-key format, ambiguous Session aggregate
ID strategy, or an unresolved `AttachListingToSession` published-status check would force those
decisions to surface mid-implementation — the failure mode the M2 and M3 retrospectives have each
flagged (rapid ADR pivots, scope drift). M4-D4 was promoted from S5 to S1 in the M4 plan refresh
because it is architecturally precedent-setting: it is the first time an Auctions handler would read
from a Listings-owned view, or conversely the first intentional Auctions-side duplicate projection.
That decision shape is too important to surface inside an implementation session.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M4-auctions-bc-completion.md` | Milestone scope — S1 deliverables live in §2, §5, §6, §8, §9; M4-D1/D2/D4 dispositions in §8 |
| `docs/decisions/007-uuid-strategy.md` | Gate 4 lives here — second formal re-evaluation; close with decision or re-defer with specific new trigger |
| `docs/decisions/README.md` | ADR index — row for ADR 007 needs update; note ADR 014 (Cross-BC read-model extension shape) is reserved for S6, and ADR 015 is reserved if M4-D4 resolves to cross-BC read |
| `docs/workshops/002-auctions-bc-deep-dive.md` | Workshop Phase 1 resolutions — Session aggregate design, Proxy Bid Manager architecture, `SessionStarted → BiddingOpened` fan-out (Option B) |
| `docs/workshops/002-scenarios.md` | Contract shape source — §4 (11 proxy scenarios) and §5 (7 session scenarios); §4.1 specifies the composite key format for M4-D1 |
| `docs/skills/integration-messaging.md` | L2 discipline — contracts carry complete payload for all future consumers at first commit |
| `docs/skills/marten-event-sourcing.md` | UUID v5 mechanics for the composite-key saga (M4-D1); UUID v7 for the Session aggregate stream ID (M4-D2) |
| `src/CritterBids.Contracts/Selling/ListingPublished.cs` | Canonical reference shape for contract stubs — namespace, docstring consumer-list format, `sealed record` style |
| `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` | Existing minimal stub from M3-S5b — S1 extends it to the full M4 payload, not a fresh authoring |

Nine entries — above the "longer than seven" guidance in the prompts README, but two are pre-existing
files consulted as reference shapes rather than substantive reads. Net substantive load is seven.

---

## In scope

- **ADR 007 Gate 4 — second formal re-evaluation.** Gate 4 was deferred in M3-S1 with "JasperFx
  guidance pending" dated rationale. M4-S1 is its final scheduled re-evaluation before the Auctions
  BC closes. Two acceptable outcomes: (a) close with a decision — JasperFx input received, event
  row ID strategy selected (UUID v7 or engine-assigned), amended into ADR 007 with the date and
  source of the guidance; or (b) re-defer with a specific downstream trigger and a named owner for
  the nudge (e.g. "re-evaluate at M5-S1 when Settlement BC lands, the last Marten BC; Erik owns the
  JasperFx follow-up"). A bare re-deferral without a new trigger and owner is **not** acceptable —
  letting Gate 4 drift past M4 becomes stale governance. Record as a new amendment section in
  ADR 007, following the same shape as the existing "Stream ID Decision — Accepted" and "Event Row
  ID Decision" (M3-S1) sections. Update the ADR 007 row in `docs/decisions/README.md`.

- **M4-D1 — proxy saga composite key format.** Confirm the Workshop 002 §4.1 string form
  `$"{ListingId}:{BidderId}"` as the deterministic name input for UUID v5 generation, and pin it in
  code by creating `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` with:
  - A `static class AuctionsIdentityNamespaces` (or equivalent naming per M3 precedent if one already
    existed elsewhere — confirm no clash by grep before creating).
  - A `public static readonly Guid ProxyBidManagerSaga` namespace Guid, generated once and committed
    as a hard-coded value. Triple-slash docstring names M4-D1 as the authorizing decision and
    Workshop 002 §4.1 as the source.
  - No Proxy-saga logic, no `UuidV5.Create(...)` helpers — pure constant file. S3 authors the saga
    and the helper that consumes this namespace.

  Record the M4-D1 disposition in the M4 milestone doc §8 (update the row from "Resolve in S1" to
  "Resolved in S1" with the pinned format). If the disposition differs from the Workshop 002 §4.1
  string form (e.g. byte-concatenation is chosen instead), stop and flag — this is a milestone-level
  divergence, not an S1 decision.

- **M4-D2 — Session aggregate ID strategy.** Confirm UUID v7 (`Guid.CreateVersion7()`) per the
  disposition in §8 — no natural business key exists (titles are not unique identifiers), and v7 is
  consistent with every other event-sourced aggregate in the codebase per ADR 007's stream ID
  section. Record by updating the §8 row from "Resolve in S1" to "Resolved in S1" with the pinned
  choice and one-line rationale. No code ships for this — Session aggregate shape is S5.

- **M4-D4 — `AttachListingToSession` published-status check mechanism.** The four options from the
  M4 plan §8 M4-D4 row: (1) cross-BC query of `CatalogListingView` directly from Auctions;
  (2) lightweight Selling-side "listing-available" signal event Auctions subscribes to;
  (3) accept handler-time ambiguity (no guard); (4) Auctions-side duplicate `PublishedListings`
  projection subscribing to the same `ListingPublished` / `ListingWithdrawn` events Listings does.
  Pick one with rationale. Record in the M4 milestone doc §6 (update the `AttachListingToSession`
  bullet to name the chosen mechanism) and §8 (row moves from "Resolve in S1" to "Resolved in S1").

  - **If the resolution is option 1 (cross-BC read):** flag ADR 015 candidate (title:
    "Cross-BC Read Access from Handlers") and add a one-line entry to `docs/decisions/README.md`
    under a new "Proposed for later authorship" marker or comment — the ADR itself is authored at
    S7 latest, earlier if the decision compounds before then. The reason this needs an ADR is in
    the M4 plan §9 risk watch-item 3 and the §8 M4-D4 disposition body.
  - **If the resolution is option 4 (duplicate projection):** S1's prompt acknowledges the residual
    lands in S5 as extra event-subscription wiring + catch-up + tests, and the S5 scope line in the
    M4 milestone doc is updated accordingly (§9 session sizing note already flags this trigger for
    S5b). No ADR triggered — duplicate projections across BCs are a named pattern already.
  - **If resolution is option 2 or 3:** record the rationale; no downstream triggers.

- **Author six new `sealed record` contract stubs in `src/CritterBids.Contracts/Auctions/`**, one
  file per event. Each file: namespace `CritterBids.Contracts.Auctions`, `sealed record` with fields
  required for every future consumer per integration-messaging L2, triple-slash summary naming the
  publisher, transport queue (where known), and consumer list. Contract shapes are final at S1
  close — S3 through S6 consume them as-is.

  | File | Purpose |
  |---|---|
  | `RegisterProxyBid.cs` | Command carrying `ListingId`, `BidderId`, `MaxAmount` — starts the Proxy Bid Manager saga |
  | `ProxyBidRegistered.cs` | Audit event emitted when a proxy saga starts — consumed by Relay (post-M5) for "proxy registered" notification |
  | `ProxyBidExhausted.cs` | Emitted when proxy's MaxAmount or credit ceiling is exceeded — consumed by Relay (post-M5) for the distinct "your proxy has been exceeded" notification per Workshop 002 Phase 1 (W001 Parked #3 resolution). Payload: `ListingId`, `BidderId`, `MaxAmount`, `ExhaustedAt` |
  | `SessionCreated.cs` | Flash-format session audit event — `SessionId`, `Title`, `DurationMinutes`, `CreatedAt`; consumed by Listings for catalog session fields (M4-S6), Operations (post-M5) for live-board |
  | `ListingAttachedToSession.cs` | Session membership event — `SessionId`, `ListingId`; consumed by Listings (M4-S6) |
  | `SessionStarted.cs` | Session kick-off event — `SessionId`, `ListingIds[]`, `StartedAt`; consumed internally by Auctions's `SessionStartedHandler` (M4-S5 fan-out) and by Listings (M4-S6) |

- **Extend the existing `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` stub** to the full
  M4 payload: `ListingId`, `WithdrawnBy` (participant or ops-staff identifier), `Reason` (optional),
  `WithdrawnAt`. Update the docstring to record that the Selling-side publisher lands in M4-S2
  (replace the "deferred per M3 milestone doc §3" wording with the M4-S2 authorship pointer) and
  that the existing field (`ListingId`) is preserved for contract-versioning hygiene (ADR 005 — any
  M3-era consumer compiled against the single-field shape continues to deserialize the extended
  record). Leave the existing consumer list intact; add the `WithdrawnBy` / `Reason` / `WithdrawnAt`
  fields' rationale in a short docstring addendum.

- **Expose the Aspire RabbitMQ management UI port** in `src/CritterBids.AppHost/Program.cs` —
  M3-S7's operational smoke test noted AMQP (5672) is exposed but the management UI (15672) is not.
  Use the Aspire-native `WithManagementPlugin()` extension (or the equivalent
  `.WithEndpoint(targetPort: 15672, ...)` shape if the plugin extension is unavailable in the pinned
  Aspire version) on the existing `rabbitMq` builder. No other AppHost changes.

- **Session retrospective** at
  `docs/retrospectives/M4-S1-auctions-completion-foundation-decisions-retrospective.md`.

---

## Explicitly out of scope

- **Any M4 handler or aggregate implementation.** No `Session` aggregate, no `ProxyBidManagerSaga`,
  no `SessionStartedHandler`, no `WithdrawListing` command handler, no `SessionMembershipHandler`.
  S2 through S6 own all of that.
- **Wolverine routing rules in `Program.cs`.** The M4 plan §5 lists three new publisher-side
  routing rules (`ListingWithdrawn` to two queues, session trio to one queue). Those land in S2
  (Selling producer) and S5 (Auctions publisher). S1 does not touch `CritterBids.Api/Program.cs`.
- **ADR 014 authoring.** ADR 014 (Cross-BC Read-Model Extension Shape) is authored in S6 alongside
  the second lived Path A application. S1 only reserves the number in the ADR index via a comment
  or placeholder row if one is not already present — do not draft the ADR body.
- **Skill file retrospective updates.** `wolverine-sagas.md` composite-key section lands at S3 close.
  `marten-projections.md` §7 reinforcement lands at S6 close. S7 consolidates. S1 does not edit any
  skill file retroactively.
- **`AuctionsIdentityNamespaces.cs` usage or UUID v5 helpers.** The file ships as a pure constant.
  The `UuidV5.Create(namespace, name)` call (or equivalent) is authored in S3 where the saga needs
  it.
- **Contract field additions for M5-or-later events.** No `ReserveCheckCompleted`, no `PaymentAuthorized`,
  no `FulfillmentStarted` stubs. Settlement owns its own M5-S1 foundation session.
- **HTTP endpoint surface.** `[AllowAnonymous]` everywhere through M5. No endpoint authoring in S1.
- **`docs/vision/bounded-contexts.md`, `docs/milestones/MVP.md`, `CLAUDE.md` edits.** Those were swept
  in M2.5-S1 and remain current. The M4 plan refresh already updated `docs/milestones/README.md`.
- **Unplanned BC scaffolding.** M3-S1's retro flagged an unplanned `CritterBids.Auctions` scaffold
  creep during a docs-only session. M4-S1 adds `AuctionsIdentityNamespaces.cs` to an **existing**
  BC project — no new projects, no new `AddXyzModule()` wiring, no new test projects.

---

## Conventions to pin or follow

- **Contract namespace and folder:** `CritterBids.Contracts.Auctions` in
  `src/CritterBids.Contracts/Auctions/`, one `sealed record` per file. Pattern mirrors the M3-S1
  stubs exactly (see `BiddingOpened.cs`, `BidPlaced.cs`) — namespace, `sealed record`, triple-slash
  summary, explicit consumer list.
- **`ListingWithdrawn` extension follows ADR 005 additive-first versioning.** The new fields are
  appended to the existing record; deserializers compiled against the single-field M3-S5b shape
  must continue to round-trip the extended payload. No field renames, no type changes to
  `ListingId`. If the extension would require a breaking change (it shouldn't), stop and flag.
- **Contract field completeness:** each contract carries every field any future consumer needs, not
  just M4 consumers. `ProxyBidExhausted`'s Relay consumer lands post-M5 but its payload is final in
  S1. The `ListingPublished` and `ListingWithdrawn` docstring consumer-list format is the reference.
- **Gate 4 amendment shape:** the new section in ADR 007 follows the same structure as the existing
  "Event Row ID Decision" section added in M3-S1 — status line, decision paragraph, rationale,
  named owner and trigger if deferred. If the outcome is closure with a decision, add a third
  section ("Event Row ID Decision — Accepted") alongside the existing deferred one; if re-deferred,
  extend the existing section with a second dated sub-entry rather than authoring a new one.
- **`AuctionsIdentityNamespaces.cs` file placement:** under `src/CritterBids.Auctions/` (the
  existing BC project), not under `src/CritterBids.Contracts/` — it is an Auctions-internal
  implementation detail that happens to be depended on by S3. Public accessibility is `public`
  only if needed; prefer `internal` unless the saga requires cross-project access (it doesn't —
  both saga and namespace live in `CritterBids.Auctions`).
- **Decisions land where readers look.** Gate 4 → ADR 007 (architectural).
  M4-D1 → `AuctionsIdentityNamespaces.cs` docstring + M4 milestone doc §8 update.
  M4-D2 → M4 milestone doc §8 update only (no code ships).
  M4-D4 → M4 milestone doc §6 (`AttachListingToSession` bullet) + §8. ADR 015 candidate noted in
  `docs/decisions/README.md` only if M4-D4 resolves to option 1.

---

## Acceptance criteria

- [ ] `docs/decisions/007-uuid-strategy.md` — updated with the second Gate 4 re-evaluation outcome
  (new decision section or extended deferred section per the "Gate 4 amendment shape" convention);
  status header in the ADR reflects the updated state.
- [ ] `docs/decisions/README.md` — ADR 007 row Summary and Date columns updated to reflect the new
  Gate 4 state; if an ADR 014 placeholder row is not already present, add one with status
  "Reserved for M4-S6 authorship"; if M4-D4 resolves to option 1, add an ADR 015 placeholder row
  with status "Reserved — conditional authorship target S7 or earlier".
- [ ] `docs/milestones/M4-auctions-bc-completion.md` — §8 rows for M4-D1, M4-D2, and M4-D4 moved
  from "Resolve in S1" to "Resolved in S1" with the pinned choices and one-line rationale each;
  §6 `AttachListingToSession` bullet updated to name the chosen M4-D4 mechanism.
- [ ] `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` — new file; `static class` with a
  single `ProxyBidManagerSaga` namespace Guid constant; triple-slash docstring cites M4-D1 and
  Workshop 002 §4.1; no other members, no helper methods.
- [ ] `src/CritterBids.Contracts/Auctions/` — six new `.cs` files
  (`RegisterProxyBid.cs`, `ProxyBidRegistered.cs`, `ProxyBidExhausted.cs`, `SessionCreated.cs`,
  `ListingAttachedToSession.cs`, `SessionStarted.cs`); each is a `sealed record` in namespace
  `CritterBids.Contracts.Auctions` with final field list and triple-slash docstring containing
  publisher, transport queue (if known), and full consumer list.
- [ ] `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` — extended from the single-field M3
  shape to the full M4 payload (`ListingId`, `WithdrawnBy`, `Reason`, `WithdrawnAt`); docstring
  updated to record M4-S2 as the real producer and ADR 005 additive-change rationale for the
  extension.
- [ ] `src/CritterBids.AppHost/Program.cs` — RabbitMQ builder call chained with
  `.WithManagementPlugin()` (or the `.WithEndpoint(targetPort: 15672, ...)` equivalent); no other
  AppHost changes. Running `dotnet run --project src/CritterBids.AppHost` and hitting
  `http://localhost:<published-port>` loads the RabbitMQ management UI login screen.
- [ ] `dotnet build` — 0 errors, 0 warnings.
- [ ] `dotnet test` — 86 passing (M3 close baseline unchanged — no new tests, no deleted tests).
- [ ] `docs/retrospectives/M4-S1-auctions-completion-foundation-decisions-retrospective.md` —
  written; records the final state of each decision (Gate 4, M4-D1, M4-D2, M4-D4), the seven
  contract file paths (six new + one extended), the `AuctionsIdentityNamespaces.cs` path, any
  scope deviation, any ADR 015 candidate flagging, and a one-paragraph "what M4-S2 should know" note.

---

## Open questions

- **Gate 4 requires JasperFx input per ADR 007.** If that input is not in hand at session time, a
  bare re-deferral is not acceptable — the correct output is re-deferral with a named new trigger
  (specifically: which downstream milestone or session's landing re-triggers Gate 4) and a named
  owner for the follow-up. If neither trigger nor owner can be named honestly, stop and flag —
  this escalates to a milestone-level question, because it means the ADR has drifted into indefinite
  deferral and needs a different disposition (e.g. "accept engine-default permanently unless and
  until a specific operational signal surfaces").
- **M4-D4 resolution shape.** If the session's reading surfaces a fifth option not listed in the M4
  plan §8 M4-D4 row — for example, a native Marten cross-BC projection feature the skill files
  don't yet document — stop and flag before pinning. The four-option list is deliberate; a fifth
  option that bypasses it is a milestone-level reopening.
- **`AuctionsIdentityNamespaces.cs` location.** If the session discovers an existing
  identity-namespaces class under a different name (M3 or earlier) that it should extend rather
  than clash with, use the existing class and record the deviation. Current codebase grep shows
  no such class, but a session-time recheck is appropriate.
- **`ListingWithdrawn` breaking-change risk.** The extension from `(Guid ListingId)` to a four-field
  record is expected to be additive-compatible under ADR 005. If System.Text.Json or the Marten
  event-type deserializer surprises, stop and flag — a breaking contract change this late in the
  Auctions lifecycle is a milestone-level question.

---

## Commit sequence

Five commits, in this order:

1. `docs(adr-007): second Gate 4 re-evaluation — [accepted v7 | deferred: <new trigger> owned by <name>]`
2. `docs(m4): resolve M4-D1, M4-D2, M4-D4 in milestone plan §6, §8; flag ADR 015 candidate if applicable`
3. `feat(auctions): pin Proxy Bid Manager composite-key namespace (AuctionsIdentityNamespaces)`
4. `feat(contracts): add six Auctions M4 integration event stubs; extend ListingWithdrawn to M4 payload`
5. `chore(apphost): expose RabbitMQ management UI port; write M4-S1 retrospective`

Commit 3 uses `feat` because `AuctionsIdentityNamespaces.cs` is a real `.cs` file shipping in the
`CritterBids.Auctions` assembly — same reasoning as the M3-S1 contract-stubs commit. Commit 5
bundles the AppHost fix (fifteen minutes of effort per the M4 plan) with the retrospective because
splitting them produces two trivial PRs; a reviewer evaluates both together in under five minutes.
