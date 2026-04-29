# Narrative 002 - Findings

Findings surfaced while authoring `002-winner-clears-settlement.md` against W003 (`003-settlement-bc-deep-dive.md` and `003-scenarios.md`) under the forward-spec posture (Settlement BC unshipped, M5 ship target). Each finding is routed via the four-lane discipline established in ADR 016 and detailed in the narrative-authoring prompt at `docs/prompts/narratives/002-winner-clears-settlement.md`:

| Lane | Resolved in this PR? |
|---|---|
| `narrative-update` | Yes. Narrative edited. |
| `workshop-update` | Sometimes. F003 minimum-scope resolved here; F004 and F005 deferred to a W003 follow-up PR per the user's minimum-scope lean. |
| `code-update` | Not applicable to narrative 002. Settlement BC is unshipped; zero `code-update` findings against Settlement, by structural impossibility. |
| `document-as-intentional` | Yes. Relationship documented in F002. |

Lane mix: 1 `narrative-update`, 3 `workshop-update`, 1 `document-as-intentional`, 0 `code-update`. The forward-spec posture flipped the expected distribution toward workshop-grade findings, exactly per the prompt's "Heads-up sources of likely findings" preview.

---

### Finding 001 - Narrative 001 Moment 8 carries three saga-event payload mis-citations against `003-scenarios.md`

**Routing:** narrative-update

**Surfaced at:** Moment 1 (`SettlementInitiated`), Moment 2 (`ReserveCheckCompleted`), Moment 3 (`WinnerCharged`)

**Discrepancy.** Narrative 001 Moment 8's `Response.` paragraph cites three Settlement saga events with field names that do not match `003-scenarios.md`:

- `SettlementInitiated`: narrative 001 renders `HammerPrice: $55.00`; scenario §1.1 uses `Price: 85.00, Source: Bidding`. The `Price` field is source-agnostic - Bidding source equals hammer price, BIN source equals BIN price - and `Source: Bidding | BuyItNow` is the disambiguator. Narrative 001 omitted `Source` and renamed `Price` to `HammerPrice` at initiation grain.
- `ReserveCheckCompleted`: narrative 001 renders `Result: "Met"`; scenario §2.1 uses `WasMet: true`. Boolean rendering is canonical per the scenarios; the string-typed `Result` form is narrative 001's invention.
- `WinnerCharged`: narrative 001 renders `AmountCharged: $55.00, RemainingCredit: $445.00, ChargedAt`; scenario §3.1 uses `Amount, ChargedAt` only. The `RemainingCredit` field has no scenarios-defined home on this event; the bidder-visible $445.00 number is delivered via the read-side `SettlementCompleted` broadcast (`{ ..., remainingCredit: 445.00 }`), not via the saga's domain event payload.

All three deviations live in the same `Response.` paragraph of narrative 001 Moment 8.

**Resolution.** Narrative 001 Moment 8's `Response.` paragraph edited in this PR. Three field-level corrections:

- `SettlementInitiated { ... HammerPrice: $55.00, InitiatedAt }` rewritten to `SettlementInitiated { ... Price: $55.00, Source: Bidding, InitiatedAt }`.
- `ReserveCheckCompleted { ... Result: "Met", CompletedAt }` rewritten to `ReserveCheckCompleted { ... WasMet: true, CompletedAt }`.
- `WinnerCharged { ... AmountCharged: $55.00, RemainingCredit: $445.00, ChargedAt }` rewritten to `WinnerCharged { ... Amount: $55.00, ChargedAt }`.

The narrative's prose claim that SwiftFerret42's remaining credit lands at $445.00 is unchanged; the $445.00 number is bidder-visible via the Relay broadcast in the same Moment, not via the `WinnerCharged` event payload. Phase 5 §7's "no re-authoring narrative 001; cite-and-edit single-paragraph fixes are permitted" gate authorizes the resolution.

---

### Finding 002 - W003 Price / HammerPrice rename across initiation is intentional but undocumented

**Routing:** document-as-intentional

**Surfaced at:** Moment 1 (initiation event reading scenario §1.1 uses `Price`); confirmed at Moment 2 (evolver §7.1 writes `HammerPrice` on state, downstream §2.1 event uses `HammerPrice`)

**Discrepancy.** W003's command and event vocabulary uses two different names for the final accepted price across the saga's lifecycle:

- `InitiateSettlement` command (§1.1 input): `Price: 85.00`
- `SettlementInitiated` event (§1.1 output, §7.1 evolver input): `Price: 85.00`
- `SettlementState.Initiated` state (§7.1 evolver output): `HammerPrice: 85.00`
- `ReserveCheckCompleted` event (§2.1): `HammerPrice: 85.00`
- All downstream events: `HammerPrice`

The asymmetry is deliberate: pre-initiation, the field name must accommodate both Bidding source (where the value is the hammer price) and BIN source (where the value is the BIN price); the generic `Price` covers both. Post-initiation, once `Source` is committed in state, the value semantically *is* the hammer price by definition (Bidding source) or the BIN price absorbed-as-hammer-equivalent (BIN source via §1.2 / §7.2). The evolver's responsibility at §7.1 is to take the source-agnostic `Price` from the event and rename it to `HammerPrice` in state.

W003 does not document this rename. A reader of W003 §1 alone may interpret the `Price` / `HammerPrice` asymmetry as a workshop inconsistency, especially when §2.1's `ReserveCheckCompleted` payload uses `HammerPrice` while the state §7.1 hydration is the only place the rename happens.

**Resolution.** Routed `document-as-intentional`. The convention is correct as designed; the workshop documentation is incomplete. Narrative 002 renders `Price` / `Source` on initiation events and `HammerPrice` on downstream events per scenarios verbatim, preserving the asymmetry; that asymmetry is the entrypoint that surfaces F002 to the W003 follow-up PR. Resolution deferred: the W003 follow-up should add a §"Field Name Convention" note to Phase 1 (or to §7's evolver narrative) explaining the rename and naming the rationale (BIN-source flexibility at initiation, post-initiation specificity).

---

### Finding 003 - W003 storage-layer references Polecat and SQL Server, predating ADR 011 (All-Marten Pivot)

**Routing:** workshop-update

**Surfaced at:** Moment 1 (PendingSettlement framing); confirmed at session close (W003 Phase 1 Part 1, Ubiquitous Language Financial Event Stream entry, "What Prior Workshops Established" storage line)

**Discrepancy.** W003 carries three storage-layer references that predate ADR 011 (All-Marten Pivot, per `CLAUDE.md`'s "All eight BCs use PostgreSQL via Marten" guidance):

- W003 §"What Prior Workshops Established": "Storage: SQL Server via Polecat - financial event streams, audit reporting."
- W003 Phase 1 Part 1 (PendingSettlement projection): "Settlement maintains its own projection built from `ListingPublished` events. When `ListingPublished` arrives over the bus, Settlement's projection handler writes a row to a `pending_settlements` table in SQL Server (Polecat)." Plus a `@BackendDeveloper` note: "Marten and Polecat have feature parity for projections. The projection class looks essentially identical; only the session type and configuration differ. No special Polecat patterns needed here."
- W003 §"Ubiquitous Language" Financial Event Stream entry: "Polecat-backed; never deleted; persists for compliance and audit."

ADR 011's pivot makes Settlement BC PostgreSQL via Marten alongside the other seven BCs. The Polecat / SQL Server framing in W003 reflects the pre-pivot architectural posture and is stale.

**Resolution.** W003 minimum-scope edits in this PR per the user's Q4 minimum-scope lean:

- W003 Phase 1 Part 1 framing: SQL Server (Polecat) replaced with PostgreSQL (Marten); the `@BackendDeveloper` note rewritten to acknowledge that the All-Marten Pivot makes the Polecat-vs-Marten comparison moot, and that the projection is a standard Marten document projection.
- W003 §"Ubiquitous Language" Financial Event Stream entry: Polecat-backed replaced with Marten-backed (PostgreSQL).

The §"What Prior Workshops Established" storage line is left untouched in this PR; rewriting it requires reframing the prior-workshops narrative which exceeds minimum-scope. The broader sweep (Polecat references in W003 Phase 2 storytelling, Phase 3 scenarios cross-references, and any remaining Phase 1 mentions outside Part 1) is deferred to a future workshop-cleanup session.

---

### Finding 004 - `SettlementInitiated` event payload differs between scenarios §1.1 (decider output) and §7.1 (evolver input)

**Routing:** workshop-update

**Surfaced at:** Moment 2 (reading §7 evolver to confirm state-hydration mechanics)

**Discrepancy.** `003-scenarios.md` shows `SettlementInitiated` with two different payload shapes across sections:

- §1.1 (decider output): `SettlementInitiated { SettlementId, ListingId, WinnerId, SellerId, Price, Source, InitiatedAt }` - seven fields.
- §7.1 (evolver input): `SettlementInitiated { Source, SettlementId, ListingId, WinnerId, SellerId, Price, ReservePrice, FeePercentage }` - eight fields, including `ReservePrice` and `FeePercentage`.

The §7.1 form is required for the evolver to hydrate `SettlementState.Initiated` with the `ReservePrice` and `FeePercentage` fields that subsequent saga phases read from state (`CheckReserve` reads `ReservePrice`; `CalculateFee` reads `FeePercentage`). The §1.1 form either truncates the payload for example brevity or is incomplete; whichever, the inconsistency between sections will mislead a reader implementing the saga from the workshop alone.

A secondary symptom: §4.1 (`FinalValueFeeCalculated`) omits `SettlementId` from the event payload while §3.1 (`WinnerCharged`) and §5.1 (`SellerPayoutIssued`) include it; this is a smaller-scale version of the same issue, suggesting that scenario-level payload consistency is generally underspecified.

**Resolution.** Routed `workshop-update`. Resolution deferred to a W003 follow-up PR per the user's minimum-scope lean. The follow-up should normalize `SettlementInitiated`'s example payload to the §7.1 longer form across §1.1, §1.2, and §1.3, and audit the SettlementId-renders across §3.1, §4.1, §5.1, and §6.1 for consistency. Narrative 002 renders `SettlementInitiated` with `Source` per §1.1 and `ReservePrice` / `FeePercentage` carried in state per §7.1; the rendering is consistent with both readings of the workshop and does not commit narrative 002 to either truncation interpretation.

---

### Finding 005 - W003 lacks a named bidder-credit projection backing the bidder's credit-balance display

**Routing:** workshop-update

**Surfaced at:** Moment 3 (the credit-debit beat; the bidder's $445.00 visible balance has no W003-defined backing read model)

**Discrepancy.** W003 names two Settlement-side read structures: `PendingSettlement` (Phase 1 Part 1, the pre-settlement cache seeded from `ListingPublished`) and the Financial Event Stream (Ubiquitous Language entry, the per-settlement audit log). Neither is the structure a bidder reads to see her credit-balance display update from $500.00 to $445.00 after `WinnerCharged` lands.

Narrative 001 Moment 8 references "Settlement's bidder-credit projection" with a definite article, treating it as a named system component; W003 does not authorise the name. Narrative 002 Moment 3 deliberately renders the same beat as "the bidder-credit ledger updates from $500.00 to $445.00" without naming a projection precisely to avoid overcommitting to a name W003 has not defined. The bidder-visible $445.00 reaches her phone in two paths: (1) via the Relay broadcast on `SettlementCompleted` carrying `remainingCredit: 445.00` per narrative 001's broadcast payload; (2) implicitly via a Settlement-internal projection updated by the `WinnerCharged` event. Path 1 is the visible path in the happy-path narrative; path 2 is the durable backing that survives across sessions, and is the projection W003 should name.

**Resolution.** Routed `workshop-update`. Resolution deferred to a W003 follow-up PR. The follow-up should define the bidder-credit projection's name (provisional: `BidderCreditView` or `BidderCreditLedger`), shape (likely `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)` or similar), lifecycle (initialised at `ParticipantSessionStarted` consumption with the assigned credit ceiling; updated on `WinnerCharged`), and consumer model (read by Relay's broadcast handler when composing the post-charge `SettlementCompleted` push; read directly by any future bidder-facing balance endpoint). Narrative 002's prose remains projection-name-agnostic; narrative 001 Moment 8's "Settlement's bidder-credit projection" reference is preserved as-is on the assumption that the W003 follow-up will retroactively legitimize the name.
