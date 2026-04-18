# CritterBids Workshops Index

Workshop documents are the **primary output of Event Modeling workshops** — the artifacts produced when personas sit down together to work through slice scenarios and BC deep-dives before implementation begins.

Each numbered workshop (W{N}) produces two artifacts:

- **Scenarios** — slice-level Given-When-Then events for the workshop's scope
- **Deep dive** or **journey** — the narrative exploration of a single BC or a cross-BC user journey

Plus a shared [`PARKED-QUESTIONS.md`](PARKED-QUESTIONS.md) that captures out-of-scope decisions raised during any workshop that need revisiting later.

---

## Documents

| Workshop | Scope | Scenarios | Deep Dive / Journey |
|---|---|---|---|
| **W001** | Flash Session demo-day user journey (cross-BC) | [`001-scenarios.md`](001-scenarios.md) | [`001-flash-session-demo-day-journey.md`](001-flash-session-demo-day-journey.md) |
| **W002** | Auctions BC — DCB, Auction Closing saga, Proxy Bid Manager saga, bidding mechanics | [`002-scenarios.md`](002-scenarios.md) | [`002-auctions-bc-deep-dive.md`](002-auctions-bc-deep-dive.md) |
| **W003** | Settlement BC — reserve check, fee calculation, payout, failure paths | [`003-scenarios.md`](003-scenarios.md) | [`003-settlement-bc-deep-dive.md`](003-settlement-bc-deep-dive.md) |
| **W004** | Selling BC — seller registration, listing lifecycle, relist, cross-BC validation | [`004-scenarios.md`](004-scenarios.md) | [`004-selling-bc-deep-dive.md`](004-selling-bc-deep-dive.md) |

**Cross-workshop:**
- [`PARKED-QUESTIONS.md`](PARKED-QUESTIONS.md) — questions raised during any workshop that were deferred (out of scope for the workshop, unresolved by available information, or awaiting external input)

---

## How to Use Workshop Documents

- **When implementing a BC:** read that BC's deep-dive + scenarios first. They capture decisions made during the workshop that aren't obvious from the event vocabulary alone.
- **When authoring a session prompt:** cross-reference the relevant workshop's scenarios for the slice being built. Slice scope in workshops should match slice scope in prompts.
- **When extending the event vocabulary:** check [`docs/vision/domain-events.md`](../vision/domain-events.md) first, then the workshop that introduced the event for the original context.

---

## Running a Workshop

Workshop methodology — facilitation, persona activation, phase structure — lives in [`docs/skills/event-modeling/SKILL.md`](../skills/event-modeling/SKILL.md). Personas live in [`docs/personas/`](../personas/).

---

## Naming Convention

Workshops are numbered sequentially with a zero-padded three-digit prefix and a paired convention:

```
{NNN}-scenarios.md                       — slice scenarios
{NNN}-{bc}-bc-deep-dive.md               — BC-focused workshop output
{NNN}-{slug}-journey.md                  — user-journey workshop output
```

The next workshop is **W005**. Check this index before creating one to confirm the next available number.

---

## When to Add a New Workshop Document

Create a new `W{N+1}-*` pair (scenarios + deep-dive or journey) when:
- A new BC enters active design
- A cross-BC journey needs exploration before implementation
- An existing BC's scope expands significantly beyond its original workshop

Use [`PARKED-QUESTIONS.md`](PARKED-QUESTIONS.md) for questions that surface during any workshop but don't belong in that workshop's scope — don't start a new workshop document just to park a question.

---

## What Does *Not* Belong Here

- **Implementation patterns** — belong in [`docs/skills/`](../skills/)
- **Event vocabulary updates** — belong in [`docs/vision/domain-events.md`](../vision/domain-events.md) (verified against workshop output, but the vocabulary lives in `vision/`)
- **Architectural decisions with alternatives** — belong in [`docs/decisions/`](../decisions/)
- **Session retros** — belong in [`docs/retrospectives/`](../retrospectives/)

---

## References

- [`CLAUDE.md`](../../CLAUDE.md) — documentation hierarchy and routing
- [`docs/skills/event-modeling/SKILL.md`](../skills/event-modeling/SKILL.md) — workshop methodology
- [`docs/personas/README.md`](../personas/README.md) — persona roster
- [`docs/vision/domain-events.md`](../vision/domain-events.md) — event vocabulary (Phase 1 workshop reference)
- [`docs/vision/bounded-contexts.md`](../vision/bounded-contexts.md) — BC ownership (verify slice assignments)
