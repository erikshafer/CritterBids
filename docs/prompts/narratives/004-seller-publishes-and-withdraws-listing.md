# Prompt 004 - Author the Selling-BC Backfill Narrative: Seller Publishes and Withdraws Listing

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-29 |
| **Phase** | Foundation Refresh, Phase 5, Item 1c |
| **Subdirectory** | `docs/prompts/narratives/` |
| **Journey** | A seller registers, drafts and publishes one listing that goes to auction, drafts and publishes a second listing, then withdraws the second listing before any session attaches (happy path) |
| **Protagonist** | GreyOwl12 (offstage in narrative 001 Cast → onstage here as the keyboard's seller) |
| **Target artifact** | `docs/narratives/004-seller-publishes-and-withdraws-listing.md` (to be produced) |
| **Companion artifact** | `docs/narratives/004-findings.md` (to be produced; conscious-skip note acceptable if zero findings) |
| **Source-of-truth dependencies** | W004 (`004-selling-bc-deep-dive.md`) and W004 scenarios (`004-scenarios.md`); lived `src/CritterBids.Selling/` code; M2-S2 (BC scaffold), M2-S5 (slice 1.1 create-draft), M2-S6 (slice 1.2 submit), M2.5-S2 (update-draft) retros; M4-S2 implementation prompt at `docs/prompts/M4-S2-selling-withdraw-listing.md` (forward-spec ground for the WithdrawListing Moment) |
| **Workflow position** | Third of four Phase 5 backfill narratives. First seller-perspective narrative for CritterBids. Mixed posture: lived M2 listing pipeline plus forward-spec M4-S2 WithdrawListing. |

---

## Framing

This session authors the Selling BC's first dedicated narrative and CritterBids' first seller-perspective narrative across the entire library. Where narratives 001-003 all carried bidder-perspective protagonists (SwiftFerret42, BoldPenguin7), narrative 004 brings GreyOwl12 — known offstage in narratives 001 and 002 as the keyboard's seller — onstage as the protagonist. The system surface he sees is the seller-side of the listing lifecycle: drafts, submissions, automated approvals, publication, and post-publication withdrawal. He never sees the auction itself; that lives in narratives 001 and 002.

Narrative 004 mixes two postures within a single journey. Moments 1-3 dramatise the lived M2 listing pipeline (`CreateDraftListing`, `SubmitListing`, the auto-approval handler, `ListingPublished`) against shipped Selling BC code at `src/CritterBids.Selling/`. Moment 4 dramatises the M4-S2 WithdrawListing flow as forward-spec — the implementation prompt lives at `docs/prompts/M4-S2-selling-withdraw-listing.md` (305 lines, authored at the session that did *not* run M4-S2's slice; the implementation has not shipped). The narrator renders the WithdrawListing Moment per the M4-S2 prompt's specification and the W004 §4 "End Early and Relist" workshop framing; the lived-code audit lane defers under `defer` until M4-S2 ships.

The journey requires a second listing to dramatise WithdrawListing plausibly. Narrative 001's Setting establishes that GreyOwl12 publishes the Vintage Mechanical Keyboard which then goes to auction (and to settlement in narrative 002); the keyboard itself is not withdrawn, by narrative 001's terminal outcome. Narrative 004 introduces a second listing of GreyOwl12's — a Vintage Folding Camera (or similar; concrete name confirmed at session start) — that he drafts, publishes, and then withdraws before any session attaches. The camera is sibling ground to the keyboard, not a replacement; it fits inside narrative 001's stated "three listings published in the days before the conference and attached to the operator's Flash session" because the camera was published but never attached. This second-listing decision is the most consequential session-start sign-off question.

Findings expectations match narrative 003's lived-code posture for Moments 1-3 (`code-update` is a real lane against shipped Selling code) and narrative 002's forward-spec posture for Moment 4 (`workshop-update` against W004 if §4 framing has staleness; zero `code-update` against unshipped WithdrawListing code). W004 may carry the same Polecat / SQL Server staleness narrative 002 surfaced in W003 (Finding 003 there); narrative 004's audit will confirm or refute.

ADR 016 (Spec-Anchored Development) governs throughout. For Moments 1-3, the audit floor is shipped code; for Moment 4, the audit floor is the M4-S2 prompt's specification.

---

## Goal

Author the Selling BC's backfill narrative covering GreyOwl12's seller-side journey: registering as a seller, drafting and publishing the Vintage Mechanical Keyboard listing (which proceeds to auction in narrative 001), drafting and publishing a second listing, then withdrawing the second listing before any session attaches. Audit W004, W004 scenarios, lived `src/CritterBids.Selling/` code, and the M4-S2 implementation prompt against the narrative as drafted. Route every disagreement through the four-lane findings discipline. Add per-row narrative back-references on W001 (slices 0.3, 1.1, 1.2) and W004 (the slice/section the narrative implements). Establish GreyOwl12's anchored cross-narrative values for downstream reference.

---

## Orientation files (read in order before starting)

1. `C:\Code\CritterBids\CLAUDE.md` — routing layer and global conventions.
2. `C:\Code\CritterBids\docs\narratives\README.md` — format manual v0.1.
3. `C:\Code\CritterBids\docs\narratives\001-bidder-wins-flash-auction.md` — Setting paragraph 2 specifically (establishes the keyboard's listing-time fields: starting bid $25, reserve $50, BIN $100, extended bidding 30-second trigger window with 15-second extension). Cast section also relevant for GreyOwl12's introduction.
4. `C:\Code\CritterBids\docs\narratives\002-winner-clears-settlement.md` and `002-findings.md` — narrative 002 established W003 Polecat / SQL Server staleness as F003. Narrative 004 may surface analogous workshop-staleness findings against W004.
5. `C:\Code\CritterBids\docs\narratives\003-bidder-starts-anonymous-session.md` and `003-findings.md` — the immediate-prior backfill narrative's discipline reference. Narrative 004 inherits the closing-arc shape, the lived-code audit posture, and the per-Moment disposition-tag-at-draft-time refinement.
6. `C:\Code\CritterBids\docs\workshops\004-selling-bc-deep-dive.md` and `004-scenarios.md` — the workshop the narrative implements. Phase 1 and Phase 2 (Storytelling: A Listing's Complete Lifecycle) are the principal references; §4 (End Early and Relist) is the WithdrawListing forward-spec section.
7. `C:\Code\CritterBids\docs\prompts\M4-S2-selling-withdraw-listing.md` — the M4-S2 implementation prompt, which the WithdrawListing Moment renders as forward-spec specification.
8. `C:\Code\CritterBids\docs\retrospectives\M2-S2-selling-bc-scaffold-retrospective.md`, `M2-S5-slice-1-1-create-draft-listing-retrospective.md`, `M2-S6-slice-1-2-submit-listing-retrospective.md`, `M2.5-S2-update-draft-listing-write-aggregate-retrospective.md` — design-time decisions the lived code alone does not show.

Per-Moment lived-code reads under `src/CritterBids.Selling/`:
- Aggregate: `SellerListing.cs`
- Status / format enums: `ListingStatus.cs`, `ListingFormat.cs`
- Validation: `ListingValidator.cs`
- Commands and handlers: `CreateDraftListing.cs`, `UpdateDraftListing.cs`, `SubmitListing.cs`
- Domain events: `DraftListingCreated.cs`, `DraftListingUpdated.cs`, `ListingSubmitted.cs`, `ListingApproved.cs`, `ListingRejected.cs`, `ListingPublished.cs`
- Seller side: `RegisteredSeller.cs`, `SellerRegistrationCompletedHandler.cs`, `SellerRegistrationService.cs`, `ISellerRegistrationService.cs`
- Module wiring: `SellingModule.cs`

Per-Moment forward-spec read for Moment 4:
- `docs/prompts/M4-S2-selling-withdraw-listing.md` (305 lines; the slice's authoritative spec for command shape, handler guards, event payload, cross-BC routing).

---

## Working pattern

Same interactive cadence as narratives 002 and 003. Cast and Setting first; Moment-by-Moment thereafter with sign-off and commit per beat.

For each Moment:
- Read the implementing slice from W001 or W004; for the WithdrawListing Moment, read the M4-S2 prompt instead.
- Read the matching scenarios from `004-scenarios.md` (lived Moments) or the M4-S2 prompt's spec (forward-spec Moment).
- Read the lived code path under `src/CritterBids.Selling/` (lived Moments) or note the forward-spec deferral (Moment 4).
- Read the relevant retro (M2-S5 for Moment 2 / drafting; M2-S6 for Moment 3 / submission; M2.5-S2 for any update-draft beat).
- Draft the Moment in the README's Guardrail-1 shape.
- Identify findings as the draft is written. Lived Moments may produce `code-update` (Selling-BC), `workshop-update` (W004 staleness), or `narrative-update` (against narrative 001's listing-time field claims). The forward-spec Moment may produce `workshop-update` (W004 §4) or render-cleanly with no findings.
- Sign-off, commit.

Multi-slice or multi-phase Moments grow in paragraphs, not labels. Moment 3 (submit + auto-approve + publish) is a strong candidate for a multi-paragraph `Response.` block; the saga from `SubmitListing` command to `ListingPublished` event covers three or four event types in close sequence.

---

## Voice and perspective

**Single-named-protagonist plus omniscient narrator** is locked by `docs/narratives/README.md` v0.1. Narrative 004 is the first to use a seller protagonist; the README's "single-seller" perspective slot is exercised here for the first time.

GreyOwl12 sees a different system surface than the bidders in narratives 001-003. He sees a seller dashboard (forward-spec UI; M6 territory), the listing-creation flow, the submission-and-approval state machine, the published listing's view of itself. He does not see anonymous bidders, the lot board, the live bidding feed, the auction's terminal outcome, the settlement saga, or the seller payout (his side of the post-settlement experience is the candidate for a future seller-perspective Settlement narrative; narrative 002's Moment 4 deferred this).

The narrator dramatises the seller-side state machine at finer grain than W001 sketches. W001's slices 1.1 and 1.2 collapse "draft → submit → approve → publish" into two slice references; W004's deep-dive expands this into Phase 1 (aggregate state machine), Phase 2 (lifecycle storytelling), and Phase 3 (component-grained scenarios). Narrative 004 dramatises at the journey grain between W001's coarse slices and W004's deep-dive precision.

---

## Findings discipline (mixed lived / forward-spec lane mix)

Audit floor splits by Moment:

| Lane | Lived Moments (1-3) | Forward-spec Moment (4) |
|---|---|---|
| `narrative-update` | Moderate. First-pass drafts will need correction against lived `SellerListing` aggregate behavior and W004 §1-§3 scenario specifics. | Low. The M4-S2 prompt is the spec; the narrator follows it. |
| `workshop-update` | Moderate. W004's storage-layer references may carry the same Polecat / SQL Server staleness narrative 002 surfaced in W003 (Finding 003 there). Slice 1.1 / 1.2 scenario fidelity to lived code may surface payload-shape drift. | Possible. W004 §4 (End Early and Relist) framing may not match the M4-S2 prompt's command name (`WithdrawListing` vs `EndListingEarly`); routing as `workshop-update` against W004 if the workshop's event name is stale relative to the M4-S2 spec. |
| `code-update` | **Real lane.** M2 listing-pipeline retros surfaced design decisions (`[WriteAggregate]` for update-draft, auto-approval handler chain, etc.) that may have residual misalignment with the workshop's specification. | **Structural impossibility.** WithdrawListing code is not shipped; nothing to be wrong. Cross-BC `code-update` against Listings or Auctions consumers (the integration `ListingWithdrawn` contract exists but its consumers may not handle it correctly) remains possible. |
| `document-as-intentional` | Moderate. Selling BC has shipped design decisions (e.g., automated approval rather than manual review for MVP) that may be deliberate-but-undocumented in W004. | Low. M4-S2 prompt is precise about its design intent. |

Lived `code-update` findings produce stub follow-up implementation prompts at `docs/prompts/implementations/<slug>.md` per the Phase 2.5 discipline if the resolution exceeds a one-line edit. One-line comment edits or similar trivial fixes land in-PR (per narrative 003's F001 precedent).

### Findings file shape

Same schema as narratives 001, 002, 003. Per foundation-refresh handoff §4.4.

### Heads-up sources of likely findings

Do not pre-decide outcomes. Be ready when these come up:

1. **W004 storage-layer references against ADR 011.** W003 carried Polecat / SQL Server framing that predated the All-Marten Pivot; narrative 002 surfaced it as F003. W004 was authored in the same workshop wave; check its Phase 1 architecture overview, Ubiquitous Language entries, and any storage-side framing for analogous staleness. Lean: minimum-scope correction in this PR if found, broader sweep deferred.
2. **`SellerListing` aggregate's state machine vs W004 Phase 1 Brain Dump.** The aggregate's lived state-transition logic is in `SellerListing.cs`; the workshop's Phase 1 sketches the state machine narratively. Drift between the two may surface as `workshop-update` (workshop is stale) or `code-update` (code drifted from intent).
3. **Auto-approval handler chain.** M2-S6 retro records the design choice for automated approval rather than manual review. The narrator's framing of "the system approves" should match the lived chain (`ListingSubmitted → ListingApproved` via the Selling BC's internal handler). Drift surfaces as `narrative-update` (first-pass narrative was wrong) or as `document-as-intentional` (the auto-approval-vs-manual-review choice is deliberate-but-undocumented in W004).
4. **`ListingPublished` payload-shape consistency with `CritterBids.Contracts/Selling/ListingPublished.cs`.** Narrative 002 confirmed the contract carries `(ListingId, SellerId, Title, Format, StartingBid, ReservePrice, BuyItNow, Duration, ExtendedBiddingEnabled, ExtendedBiddingTriggerWindow, ExtendedBiddingExtension, FeePercentage, PublishedAt)` — 13 fields. Narrative 004 should render the publish event with this exact shape; deviations route as `narrative-update`.
5. **WithdrawListing command name and event payload.** The M4-S2 prompt names the command `WithdrawListing` and the event `ListingWithdrawn`; W004 §4 names the operation "End Early" and the event may differ. Reconciliation routes as `workshop-update` against W004 (the M4-S2 prompt's naming is more recent and authoritative for the implementation).
6. **`ListingWithdrawn` integration-contract presence vs producer absence.** `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` exists today (the integration event is typed). The Selling BC's *producer* of this event is not shipped (M4-S2 hasn't run). The contract-without-producer state may surface as `document-as-intentional` (the contract was added speculatively) or as `code-update` (Selling BC should produce it; M4-S2 is the queued slice). The cross-BC consumers in Auctions BC may be wired against the integration event; narrative 004's audit should note the consumer-without-producer asymmetry.
7. **GreyOwl12's seller-registration timing relative to narrative 001 Setting.** Narrative 001 says listings were "published by sellers in the days before the conference"; narrative 004's seller-registration Moment must place GreyOwl12's registration *before* his draft of the keyboard listing. The exact time-relative anchoring (hours? days? weeks?) is a Setting-grain decision but should align with W001's "days before the conference" framing.

---

## Cross-reference discipline

- Each Moment cites its slice via `**Implements:** slice X.Y[, slice X.Z, ...]`. Narrative 004 implements W001 slices 0.3 (seller registration), 1.1 (draft), 1.2 (submit-and-publish). The WithdrawListing Moment cites the M4-S2 slice; if W001 has not yet been updated to reference the M4-S2 slice number, the Moment cites M4-S2 directly.
- Domain event names render in code-style backticks: `DraftListingCreated`, `DraftListingUpdated`, `ListingSubmitted`, `ListingApproved`, `ListingPublished`, `ListingWithdrawn`. Plain text for ordinary nouns: Listing, Listing Draft, Listing Submission, Auto-Approval, Publication, Withdrawal, Seller Registration.
- Do not restate `004-scenarios.md` content. Reference the workshop section number and the W001 slice number; the workshop is the test specification, the narrative is the journey.
- W001's consolidated Narrative Cross-References block already exists (extended through narrative 003 in PR #21). Narrative 004 adds a new bullet for narrative 004 implementing slices 0.3, 1.1, 1.2 (and the M4-S2 slice number if assigned).
- W004 has no Narrative Cross-References block today. Narrative 004's session adds one as a new top-level section between an appropriate boundary in the workshop. Per Phase 3 Item 2's BC-workshop default, per-row form is preferred when narrative-implemented slices are 1-3 in count; W004 is BC-focused and narrative 004 implements multiple W001 slices via a unified seller-perspective narrative — consolidated form fits because narrative 004 is the first narrative to cite W004 and the slices it implements span W004's §1, §2, possibly §3 and §4. Confirm at session start.

---

## What the narrative does NOT carry

- **No code or pseudocode.** Aggregate methods, handler signatures, validator expressions described in prose.
- **No implementation choices.** Marten primitive choices (event-sourcing for `SellerListing`, projections), Wolverine handler routing, ASP.NET endpoint conventions belong to skill files.
- **No architectural decisions.** ADR candidates surface in the deferred section.
- **No GWT test specifications.** Reference `004-scenarios.md` section numbers; do not restate.
- **No UX or UI design.** Render at the seller-experience grain ("the seller dashboard shows the draft state"); do not design the screens. The forward-spec UI for the seller dashboard is M6 frontend territory; if the narrator names UI behavior, the gap surfaces as a deferred entry under `defer` (narrative 003's Finding 002 is the precedent — the catalog-header display-name UI claim had no backend GET endpoint, routed `code-update`-and-stub).
- **No re-authoring of narratives 001, 002, or 003.** All three are `status: accepted`. Single-paragraph cite-and-edit fixes against any of the three are permitted per Phase 5 §7 if narrative 004's audit surfaces drift; structural rewrite is not.

---

## In scope (proposed Moment list)

| Moment | Slice(s) from W001 / source | Posture | Seller experience |
|---|---|---|---|
| 1 | 0.3 (seller registration) | Lived | GreyOwl12 registers as a seller. Identity gains the seller flag; the `Participant` aggregate's `IsRegisteredSeller` flips to true. |
| 2 | 1.1 (draft creation) | Lived | GreyOwl12 drafts the Vintage Mechanical Keyboard listing: title, format, starting bid $25, reserve $50, BIN $100, duration, extended-bidding settings. Per W004 §1 draft-lifecycle scenarios. |
| 3 | 1.2 (submit, auto-approve, publish) | Lived | He submits the keyboard listing; the system auto-approves and publishes. Multi-paragraph `Response.` walks `ListingSubmitted → ListingApproved → ListingPublished`. The integration event lands on the `listings-selling-events` and `auctions-selling-events` queues for Listings and Settlement BC consumption. |
| 4 | 1.1 + 1.2 (second listing pipeline, compressed) | Lived | He drafts and publishes a second listing: a Vintage Folding Camera (concrete name confirmed at session start). Compressed multi-paragraph `Response.` since the pipeline is structurally identical to Moments 2-3 — only the listing details differ. The camera is published but never attached to a session, by deliberate seller decision. |
| 5 | M4-S2 WithdrawListing | **Forward-spec** | He withdraws the camera listing before any session attaches. The `WithdrawListing` command lands per the M4-S2 prompt's spec; the `ListingWithdrawn` domain event commits; the `ListingWithdrawn` integration event publishes for Listings and Auctions BC consumers. The keyboard listing remains untouched and proceeds to its narrative-001 journey. |

Five Moments. Bookended by lived-code Moments (1 and 5 — wait, 5 is forward-spec); Moments 2-4 are lived. The forward-spec Moment 5 closes the journey.

Alternative groupings flagged at session start:

1. **Four Moments** — collapse Moments 2 and 3 into one multi-phase "GreyOwl12 publishes the keyboard" Moment with a long multi-paragraph `Response.` covering draft → submit → approve → publish in sequence. Counter-argument: the draft beat is a meaningful seller-experience shift (he's editing privately) distinct from the submission beat (he's relinquishing control to the system); collapsing flattens the journey.
2. **Six Moments** — split Moment 3 into separate "submit", "auto-approve", "publish" Moments. Counter-argument: the auto-approve happens silently from GreyOwl12's window; he sees submission-then-published with no intermediate beat to perceive. Splitting dramatises the system's saga at finer grain than the seller-experience justifies.
3. **Three Moments** — drop Moment 4 (the second listing's publication arc) and treat the camera as Setting inheritance: "GreyOwl12 also has a camera listing already published; the WithdrawListing Moment dramatises its end." Counter-argument: introducing a brand-new listing in Setting that doesn't appear in narrative 001 may feel suddenly-named; better to dramatise its publication so the reader has context for the withdrawal.

Lean: five Moments. Flag at session start if a different grain fits.

The second-listing concrete name (Vintage Folding Camera vs Antique Pocket Watch vs whatever) is also a session-start sign-off question. The chosen name anchors as part of GreyOwl12's cross-narrative ground.

---

## Out of scope for this session

- **Manual approval flow.** W004 may sketch a manual-review variant; the lived MVP is automated approval. Manual review is `post-MVP` deferral.
- **Draft revision / iteration cycles.** GreyOwl12 may iterate on his draft (update title, adjust starting bid) before submitting; the narrative may dramatise zero or one iteration but does not exhaustively cover the update-draft state machine. W004 §1 covers update-draft fully; narrative 004 references it without restating.
- **Listing rejection path.** If submission validation fails, the listing is rejected per W004 §2.2; the narrative is happy-path so rejection is `alternate-path-failure`.
- **Post-publication revision (`ListingRevised`).** W004 §3 covers post-publication revision; narrative 004's keyboard does not change after publication, and the camera is withdrawn rather than revised. Out of scope; `separate-narrative` if it warrants its own future narrative.
- **Relist after end-early.** W004 §4 covers the relist-marker pattern after End Early. The camera's withdrawal in Moment 5 is terminal; no relist. `separate-narrative`.
- **Bidder-perspective on GreyOwl12's listings.** Already covered in narratives 001 (keyboard from SwiftFerret42's window) and 003 (BoldPenguin7's session-start which would see the keyboard in the catalog); narrative 004 does not re-render bidder-side beats.
- **Settlement-side seller payout receipt.** Narrative 002 deferred the seller's receipt of `SellerPayoutIssued` as `separate-narrative`; that remains out of scope here. A future seller-perspective Settlement narrative is the natural home.
- **Operations BC's view of seller dashboards.** `separate-narrative` for any future operator-perspective work.
- **Any code refactor.** `code-update` findings produce in-PR fixes (one-line edits) or stub follow-up prompts (anything larger).
- **W001 broad backfill of narrative back-references.** Only slices the narrative directly implements get a back-reference entry.
- **Methodology format changes.** README v0.1 dialect remains locked.
- **Phase 5 cross-narrative retrospective.** Item 4 territory.

---

## Deliverable plan

Per Phase 5 prompt §3.4 acceptance gates:

1. **Narrative file** at `docs/narratives/004-seller-publishes-and-withdraws-listing.md`. Frontmatter v1, prose-paragraph Moments, single-named-seller voice. `status: accepted` at session close.
2. **Findings file** at `docs/narratives/004-findings.md`, OR a conscious-skip note in the narrative-internal retro if zero findings surface (unlikely given W004's authoring-time lineage and the Selling BC's depth).
3. **Stub follow-up prompts** at `docs/prompts/implementations/<slug>.md`, one per `code-update` finding whose resolution exceeds a one-line edit.
4. **Narratives README Index update** in `docs/narratives/README.md`. Row 004 added.
5. **W001 cross-reference extension** on the consolidated Narrative Cross-References block: a new bullet for narrative 004 listing slices 0.3, 1.1, 1.2.
6. **W004 cross-reference addition** as a new top-level Narrative Cross-References section (W004 has none today). Form: per-row or consolidated per Phase 3 Item 2 default and the workshop's structural shape. Confirm at session start.
7. **Methodology log Entry 001 candidate.** Phase 4 retro time-box; the lived-BC narratives are the chance for Entry 001. Apply the entry-criteria gate. Conscious-skip note acceptable.
8. **Narrative-internal retrospective** appended in the narrative file after `## Deferred from this narrative`.

---

## Acceptance criteria

- [ ] `docs/narratives/004-seller-publishes-and-withdraws-listing.md` exists. Frontmatter conforms to v1 vocabulary. `status: accepted`.
- [ ] Every Moment cites its slice via `Implements:`.
- [ ] No bulleted lists appear inside any Moment body (Guardrail 1).
- [ ] No frontmatter keys outside the v1 vocabulary (Guardrail 2).
- [ ] Each Moment has a `Context.`, `Interaction.`, `Response.` body. `Why this matters to the seller.` is present where it adds meaning.
- [ ] `## Deferred from this narrative` exists. Items are bucketed by the seven disposition tags.
- [ ] `docs/narratives/004-findings.md` exists with at least one finding, OR the narrative-internal retro contains an explicit conscious-skip note with rationale.
- [ ] Each `code-update` finding (if any) has a stub follow-up prompt at `docs/prompts/implementations/<slug>.md`, except for one-line comment edits resolvable in-PR.
- [ ] `docs/narratives/README.md` Index table contains row 004.
- [ ] W001's consolidated Narrative Cross-References block carries a new bullet for narrative 004 implementing slices 0.3, 1.1, 1.2.
- [ ] W004 carries a Narrative Cross-References section listing narrative 004's coverage.
- [ ] Narrative-internal retro appended.
- [ ] No file under `src/` or `tests/` was edited in this session beyond any in-PR resolution of small `code-update` findings (e.g., one-line comment edits). Larger code resolutions route to stubs.

---

## Open questions to flag (not decide)

These are session-start decisions; surface them and ask the user before locking Cast and Setting.

- **Five Moments vs four vs six.** §"In scope" leans five. Trade-offs documented in §"In scope". Lean: five.
- **Second-listing concrete name.** Working default: Vintage Folding Camera. Alternative ideas: Antique Pocket Watch, Hand-Bound Leather Journal, anything that fits a "small artisan item GreyOwl12 might list and then change his mind about." Confirm at session start; the choice anchors GreyOwl12's cross-narrative ground.
- **Slice 0.3 (seller registration) inclusion as Moment 1 vs Setting inheritance.** Default: include as Moment 1, since narrative 003's deferred list routed 0.3 as `separate-narrative` and narrative 004 is the natural home. Alternative: skip Moment 1 and treat seller-registration as Setting inheritance ("GreyOwl12 is already a registered seller from earlier in the week"). The narrative tightens to 4 Moments. Lean: include as Moment 1 — the registration beat is a meaningful seller experience worth dramatising.
- **WithdrawListing Moment forward-spec posture.** Confirmed at session-start review (per the established pattern in narratives 001, 002): the Moment renders the M4-S2 prompt's specification; lived-code audit defers under `defer`; zero `code-update` findings against unshipped WithdrawListing code. Cross-BC consumer findings remain possible.
- **W004 Polecat / SQL Server staleness audit depth.** Lean: minimum-scope correction in this PR if found (analogous to narrative 002's F003 W003 fix); broader sweep deferred.
- **PR shape: fold prompt + narrative session into one PR vs separate prompt PR.** Default per the Phase 5 Item 1a/1b precedent: fold. Confirm at session start.
- **GreyOwl12's anchored cross-narrative values.** Beyond the keyboard's listing-time fields (already established by narrative 001 Setting), what new specifics does narrative 004 anchor as canonical? Candidates: seller-registration timestamp (relative to conference), the second listing's concrete name and listing-time fields, GreyOwl12's seller-side dashboard view (forward-spec). Decide at the relevant Moment.
- **Methodology log Entry 001 trigger.** Three of the four lived-BC narratives (002, 003, 004) will have surfaced findings by session close; narrative 005 (Auctions) is the fourth and largest. Apply the entry-criteria gate at narrative 004's close. Silence remains fine.

---

## Memory inheritance

Phase 1, Phase 2, narrative 002 / 003 session memories apply unchanged. Notable carryforwards:

- **Depth over brevity** when explaining tradeoffs.
- **Ubiquitous language** (auction-domain, Selling-flavored): Listing, Listing Draft, Submission, Auto-Approval, Publication, Withdrawal, Seller, Seller Registration, Reserve, Hammer Price, Buy It Now, Extended Bidding, Listing Format (Timed, Flash).
- **DDD, CQRS, Event Sourcing, EDA** assumed background.
- **SDD and NDD methodology vocabulary is NOT assumed background.** Define on first use.
- **Lean opinions on questions.** Propose a default with rationale rather than open-ended elicitation.
- **Em-dash hygiene does NOT apply to internal docs.** Per the memory clarification at narrative 002 close, the no-em-dash convention was intended for external-facing prose only. Internal narratives, workshops, retros, ADRs, prompts, and commit messages do not need em-dash hygiene.
- **Punchy prose; no AI-tool references in committed text.**
- **No `git push` to `main`** without explicit authorization. Commit freely on the narrative-004 branch; push only when asked.

---

## Starting move

When the session begins:

1. Re-read this prompt and `docs/narratives/README.md` v0.1 in full.
2. Re-read narrative 001 Setting (paragraph 2 specifically, for the keyboard's listing-time fields) and the Cast section (for GreyOwl12's offstage characterisation).
3. Skim narrative 003 Setting and Moment 2 to absorb the lived-code audit cadence; narrative 004 inherits this discipline.
4. Skim the M4-S2 implementation prompt (`docs/prompts/M4-S2-selling-withdraw-listing.md`) to absorb the WithdrawListing spec.
5. Confirm with user: Moment grain (5 vs 4 vs 6), second-listing name, slice 0.3 inclusion, W004 cross-reference form (per-row vs consolidated), PR shape (fold vs separate). Lock these before drafting Cast.
6. Propose Cast and Setting. Sign-off. Commit.
7. Walk Moment-by-Moment per the working pattern. Lived Moments (1-4): read implementing slice → read W004 scenario section → read lived code → draft → surface findings → sign-off → commit. Forward-spec Moment 5: read M4-S2 prompt → read W004 §4 → draft → surface findings → sign-off → commit.
8. At session close: classify all findings, write any `code-update` stub prompts, update the narratives README Index, extend W001's consolidated Narrative Cross-References block, add a Narrative Cross-References section to W004, evaluate methodology-log Entry 001, append the narrative-internal retro, flip the narrative's `status:` to `accepted`.

---

## Document history

- **v0.1** (2026-04-29): Authored as foundation-refresh Phase 5 Item 1c session prompt. Adapts the Phase 5 Item 1a (narrative 002) and Item 1b (narrative 003) prompt templates for mixed lived / forward-spec posture and seller-perspective protagonist. Five-Moment proposal: lived M2 listing pipeline (Moments 1-4) plus forward-spec M4-S2 WithdrawListing (Moment 5). Second-listing requirement (a Vintage Folding Camera or similar) flagged as the most consequential session-start sign-off question, since the keyboard cannot dramatise WithdrawListing without contradicting narrative 001's terminal outcome. The Phase 5 prompt §3.4's framing of "M4-S2 WithdrawListing" as part of the "lived-code audit surface" was inaccurate — only the M4-S2 implementation prompt exists; the code is unshipped — and this prompt corrects the framing by classifying Moment 5 as forward-spec. Em-dash hygiene drop applies (no audit step).
