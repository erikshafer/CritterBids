# CritterBids Milestones Index

Milestone documents define **what ships in each phase** — scope boundaries, definition-of-done criteria, and the demo scenarios each milestone unlocks.

Older milestone documents are historical scope snapshots and may intentionally reference superseded implementation details (for example, pre-ADR 011 storage assumptions). For current runtime reality and contributor instructions, use root `README.md` and `CLAUDE.md`.

They are distinct from their peer folders:

- **`docs/milestones/`** (this folder) — *what and when* ships in each phase
- **`docs/vision/`** — *what* the system is and how its pieces fit together
- **`docs/decisions/`** — *why* specific paths were chosen (ADRs)
- **`docs/skills/`** — *how* to implement patterns in code
- **`docs/prompts/`** — the session-level task breakdowns that deliver each milestone

---

## Documents

| File | Scope |
|---|---|
| [`MVP.md`](MVP.md) | MVP definition of done — in-scope and out-of-scope inventory, demo scenario, success criteria. Start here. |
| [`M1-skeleton.md`](M1-skeleton.md) | Milestone 1 — repository skeleton, BC module scaffolding, Aspire orchestration. Foundation milestone. |
| [`M2-listings-pipeline.md`](M2-listings-pipeline.md) | Milestone 2 — listings pipeline from draft through publication. First vertical slice. |
| [`M3-auctions-bc.md`](M3-auctions-bc.md) | Milestone 3 — Auctions BC core: DCB boundary model, `Listing` aggregate, Auction Closing saga with extended bidding. Proxy Bid Manager and Session aggregate deferred to M4. |
| [`M4-auctions-bc-completion.md`](M4-auctions-bc-completion.md) | Milestone 4 — Auctions BC completion: Proxy Bid Manager saga (first composite-key saga), Session aggregate with Flash format, Selling-side `WithdrawListing` producer. Triggers ADR 014 authoring. |
| [`M5-settlement-bc.md`](M5-settlement-bc.md) | Milestone 5 — Settlement BC: payment capture, payout saga, `SettlementCompleted` integration event. First BC to use cancellable scheduled messages in a production saga path. |
| [`M6-obligations-relay-bc.md`](M6-obligations-relay-bc.md) | Milestone 6 — Obligations BC (post-sale coordination saga with cancellable reminders and dispute sub-workflow) + Relay BC (SignalR real-time push via `BiddingHub` and `OperationsHub`, notification history projection). |

---

## Reading Order

For a new contributor:
1. [`MVP.md`](MVP.md) — grounding on the end state
2. Sequence through `M1` → `M2` → `M3` → `M4` → `M5` → `M6` to see how scope stacks toward MVP

For someone picking up work on a specific milestone: read that milestone's document plus the session prompts in [`docs/prompts/`](../prompts/) tagged with the corresponding milestone prefix (e.g. `M3-S1-*.md`).

---

## When to Add a New Milestone Document

Create a new `M{N}-{slug}.md` when:
- A new milestone is scoped (definition-of-done + deliverables decided)
- A post-MVP milestone becomes concrete enough to specify (e.g. `M-transport-swap`, `M-storage-swap`)

Milestone documents describe *intent and scope*, not implementation. Implementation details live in session prompts ([`docs/prompts/`](../prompts/)) and code.

---

## What Does *Not* Belong Here

- **ADR reasoning** — trade-off analysis with alternatives considered belongs in [`docs/decisions/`](../decisions/)
- **Session-level task breakdowns** — "which slice in M3-S4" belongs in [`docs/prompts/`](../prompts/)
- **Retrospectives** — session retros belong in [`docs/retrospectives/`](../retrospectives/)
- **Implementation patterns** — belong in [`docs/skills/`](../skills/)

---

## References

- [`CLAUDE.md`](../../CLAUDE.md) — documentation hierarchy and routing
- [`docs/vision/README.md`](../vision/README.md) — vision index
- [`docs/decisions/README.md`](../decisions/README.md) — ADR index
- [`docs/prompts/README.md`](../prompts/README.md) — session prompt template and rules
- [`docs/retrospectives/README.md`](../retrospectives/README.md) — retrospective template and rules
