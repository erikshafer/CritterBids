# Narrative 004 - Findings

Findings surfaced while authoring `004-seller-publishes-and-withdraws-listing.md` against W001 slices 0.3, 1.1, 1.2; W004 (`004-selling-bc-deep-dive.md` and `004-scenarios.md`); lived `src/CritterBids.Selling/` code; and the M4-S2 implementation prompt at `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md`. Each finding is routed via the four-lane discipline established in ADR 016 and detailed in the narrative-authoring prompt at `docs/prompts/narratives/004-seller-publishes-and-withdraws-listing.md`:

| Lane | Resolved in this PR? |
|---|---|
| `narrative-update` | n/a (none surfaced) |
| `workshop-update` | n/a (none surfaced; W004 carries no Polecat / SQL Server staleness against ADR 011, in contrast to W003 which surfaced narrative 002's F003) |
| `code-update` | F002 routed to a stub follow-up implementation prompt per Phase 2.5 discipline. |
| `document-as-intentional` | F001 and F003. Both documented here; no in-PR or follow-up code change. |

Lane mix: 0 `narrative-update`, 0 `workshop-update`, 1 `code-update`, 2 `document-as-intentional`. The lived-code audit posture for Moments 1-4 surfaced two `document-as-intentional` findings (intentional-but-comment-explained design choices in lived code) plus one `code-update` finding (real gap requiring a stub follow-up). The forward-spec Moment 5 (M4-S2 WithdrawListing) surfaced no findings — the M4-S2 prompt's spec is precise and the narrator rendered it without surfacing inconsistencies.

---

### Finding 001 - `SubmitListingHandler` hardcodes `FeePercentage: 0.10m` as an M5 placeholder

**Routing:** document-as-intentional

**Surfaced at:** Moment 3 (per-Moment lived-code read of `SubmitListing.cs`)

**Discrepancy.** `src/CritterBids.Selling/SubmitListing.cs:70` constructs the outgoing `CritterBids.Contracts.Selling.ListingPublished` integration event with `FeePercentage: 0.10m` as a literal constant. The inline comment at line 70 reads `// M5 placeholder — no fee engine exists yet`. Narrative 002 confirmed via the lived contract `src/CritterBids.Contracts/Selling/ListingPublished.cs` that the integration event carries a `FeePercentage` field, and narrative 002 Moment 1 references `FeePercentage 10.0` from the `PendingSettlement` projection that is seeded by this event. The handler's hardcoding means every published listing emits the same 10.0% fee; per-seller, per-category, or fee-engine-driven variation is not possible until M5 ships and a configurable boundary replaces the constant.

The hardcoding is not a bug in the conventional sense — the comment explicitly flags it as an intentional placeholder pending downstream work. But it is worth recording so future audits do not re-surface it as a finding when M5 work begins.

**Resolution.** Routed `document-as-intentional`. No in-PR or follow-up code change in this session. The placeholder is intentional and the comment carries the design-intent evidence. When M5's Settlement BC ships and the fee engine work moves the constant into a configurable boundary (likely a Settlement-side configuration document or a per-seller / per-category fee table consumed by the Selling BC at submit time), this finding's documentation becomes historical context rather than active gap.

---

### Finding 002 - `SubmitListing` has no HTTP endpoint; the seller dashboard cannot trigger submit via HTTP

**Routing:** code-update

**Surfaced at:** Moment 3 (per-Moment lived-code read of `SubmitListing.cs` plus the surrounding `Features/`-equivalent flat-structure check for any `[WolverinePost]`/`[WolverineGet]` registrations on a SubmitListing route)

**Discrepancy.** `src/CritterBids.Selling/SubmitListing.cs:10-11` carries the inline comment `/// No HTTP endpoint in M2 — tested as an aggregate handler only (scenario 2.1–2.4).` `SubmitListingHandler.Handle` (lines 32-75) is invoked only via Wolverine-internal aggregate-handler dispatch in M2 tests; no `[WolverinePost("/api/listings/{id}/submit")]` or analogous registration exists. The seller dashboard's "Submit" button (forward-spec UI for M6 frontend MVP) requires an HTTP endpoint to dispatch the command from outside the Wolverine handler chain.

This is structurally similar to narrative 003's F002 (missing `GET /api/participants/{id}` endpoint). The pattern is the same: lived handler exists, lived integration tests exist via aggregate-handler invocation, but no HTTP route registration exposes the operation to external callers. Resolution per the Phase 2.5 discipline is a stub follow-up implementation prompt that scopes the endpoint addition.

**Resolution.** Stub follow-up implementation prompt authored at `docs/prompts/implementations/n004-fu-submit-listing-endpoint.md` per Phase 2.5 discipline. Slice scope: add a `[WolverinePost]` registration on a route like `/api/listings/{id}/submit` that dispatches the existing `SubmitListing` command to `SubmitListingHandler.Handle` against the loaded `SellerListing` aggregate. Returns 200 OK on the happy path; 400/409/422 on the various rejection paths (state-guard violations, validation failures). The handler does not change; only the HTTP entry point is added. Resolution runs in subsequent product work.

---

### Finding 003 - `ListingStatus` enum has no `Approved` intermediate state; auto-approve-and-publish compresses 3 logical states into 2 observable states

**Routing:** document-as-intentional

**Surfaced at:** Moment 3 (per-Moment lived-code read of `ListingStatus.cs` and `SellerListing.cs`)

**Discrepancy.** `src/CritterBids.Selling/ListingStatus.cs:9-16` defines the enum with five values: `Draft, Submitted, Published, Rejected, Withdrawn`. There is no `Approved` value. `src/CritterBids.Selling/SellerListing.cs:56-57` flips `Status` to `ListingStatus.Published` directly on `Apply(ListingApproved)`, skipping any intermediate `Approved` state. Then `Apply(ListingPublished)` (lines 62-66) flips `Status` to `Published` again (idempotent) and sets `PublishedAt`.

The state machine logically has three steps in the auto-approval-and-publication flow: `Submitted → Approved → Published`. The lived encoding compresses this into two observable states: `Submitted → Published` (with both `ListingApproved` and `ListingPublished` events landing atomically in the same transaction). Downstream consumers cannot observe the "approved-but-not-yet-published" intermediate state because no enum value or aggregate snapshot exposes it.

The compression is intentional. `ListingStatus.cs:5-7`'s comment notes: `Only the Draft transition is implemented in S5. Submitted, Published, Rejected, and Withdrawn transitions arrive with their respective handlers in S6+. All statuses are defined now so that state-guard tests (1.4, 1.5) and the ListingStatus enum are complete at M2 close.` The comment confirms the enum's intentional 5-state shape; the Approved-skipping is part of that design.

**Resolution.** Routed `document-as-intentional`. No in-PR or follow-up code change. The compression is intentional and the comment carries the design-intent evidence. W004's Phase 1 aggregate-state-machine sketch may benefit from explicitly naming the compression so a future reader does not interpret the missing state as an oversight; that is a future workshop-cleanup edit, not a Phase 5 deliverable. If a future product feature requires the "approved-but-not-yet-published" state to be observable (e.g., a delayed-publication scheduler, a manual-review UI), this finding is the entry point that will need to be reopened.
