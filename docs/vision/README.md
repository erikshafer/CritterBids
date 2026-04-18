# CritterBids Vision Index

Vision documents describe **what CritterBids is and how its pieces fit together** — the intent the code is expected to realize. They are the canonical answer to "why does this project exist, what are its parts, and how do they talk to each other?"

They are distinct from their peer folders:

- **`docs/vision/`** (this folder) — *what* the system is, its domain vocabulary, and its topology
- **`docs/decisions/`** — *why* specific paths were chosen over alternatives (ADRs)
- **`docs/skills/`** — *how* to implement patterns in code
- **`docs/milestones/`** — *when* scope lands across the delivery timeline

Code is the source of truth once it exists. Vision documents describe intent; when the two disagree, update the vision document to match the code (or decide which to revise via an ADR).

---

## Documents

| File | Purpose | When to read |
|---|---|---|
| [`overview.md`](overview.md) | Project vision, the auction-domain rationale, the eBay model, demo-first philosophy | First contact with the project; grounding before any architectural discussion |
| [`bounded-contexts.md`](bounded-contexts.md) | Each of the eight BCs — purpose, ownership, storage, integration points; the BC map and integration topology | Before adding a feature, a BC, or a cross-BC integration event; any time a BC boundary is in question |
| [`domain-events.md`](domain-events.md) | Canonical event vocabulary across all BCs — internal vs integration, meaning, and the naming conventions that govern every event type | Before authoring a new domain event, during Event Modeling workshops (Phase 1 vocabulary check), and when reviewing a PR that introduces events |
| [`live-queries-and-streaming.md`](live-queries-and-streaming.md) | Forward-looking architectural framing for reactive data delivery — projection side effects (available today) vs Wolverine `StreamAsync` + Marten 9 live queries (future) | Before wiring any real-time or live-view surface; informs future ADRs once upstream APIs stabilize |

---

## What does *not* belong in `docs/vision/`

- **ADR-level reasoning** — trade-off analysis with alternatives considered belongs in `docs/decisions/`. A vision document may reference an ADR but should not re-litigate it.
- **Implementation patterns** — handler shapes, saga scaffolding, test fixtures belong in `docs/skills/`.
- **Milestone scope** — "which slice ships in M3" belongs in `docs/milestones/`.
- **Session records** — session prompts and retros belong in `docs/prompts/` and `docs/retrospectives/`.

If a vision document starts growing code samples, milestone callouts, or option tables with pros and cons, that content likely belongs elsewhere.

---

## When to Update

Update a vision document when:

- A BC boundary, storage assignment, or integration direction changes (usually alongside an ADR)
- A domain event is added, renamed, retired, or has its meaning refined
- The demo-day scenario, eBay-model framing, or project philosophy shifts
- The reactive architecture story shifts (new Critter Stack primitive, new upstream release)

Historical edits made in response to specific sessions (e.g. the ADR 011 storage-prose cleanup) are recorded in the session retrospective, not inside the vision document itself — vision documents describe the current intent, not their own changelog.

---

## References

- [`CLAUDE.md`](../../CLAUDE.md) — documentation hierarchy and routing
- [`docs/decisions/README.md`](../decisions/README.md) — ADR index
- [`docs/skills/README.md`](../skills/README.md) — skills index (implementation patterns)
