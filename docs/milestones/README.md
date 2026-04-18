# CritterBids Milestones Index

Milestone documents define **what ships in each phase** — scope boundaries, definition-of-done criteria, and the demo scenarios each milestone unlocks.

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
| [`M3-auctions-bc.md`](M3-auctions-bc.md) | Milestone 3 — Auctions BC core: DCB, Auction Closing saga, Proxy Bid Manager saga, bidding mechanics. |

---

## Reading Order

For a new contributor:
1. [`MVP.md`](MVP.md) — grounding on the end state
2. Sequence through `M1` → `M2` → `M3` to see how scope stacks toward MVP

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
