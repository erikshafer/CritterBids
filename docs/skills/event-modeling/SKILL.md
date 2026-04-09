---
name: event-modeling-workshop
description: >
  Facilitate an Event Modeling workshop session for designing information systems.
  Use this skill when the user wants to run, simulate, plan, or get guidance on an
  Event Modeling session — including brain dumps, timeline construction, slice
  definition, scenario writing, or any phase of the Adam Dymitruk-style workshop.
  Also use when the user asks about multi-persona facilitation of an Event Modeling
  session, or wants Claude to play multiple roles (facilitator, domain expert,
  developer, skeptic) during the exercise.
---

# Event Modeling Workshop Skill

Event Modeling is a collaborative workshop technique created by Adam Dymitruk (Adaptech Group)
for designing information systems. It produces a visual, timeline-based blueprint showing how
data flows through a system — from user intent through state changes to read-side projections.
It works for any information system, not just event-sourced ones, but maps naturally onto
CQRS and event sourcing patterns.

## The Four Building Blocks

| Block | Color | Meaning |
|---|---|---|
| **Events** | Orange | Facts that occurred — past tense, immutable |
| **Commands** | Blue | User intentions or system requests that cause events |
| **Views / Read Models** | Green | Projections of event data back to the UI |
| **UI Wireframes / Screens** | White | What the user actually sees and interacts with |

Arrange these in chronological order on a horizontal timeline, in swim lanes:
`UI → Command → Event Stream → View → UI`

---

## Workshop Phases

### Phase 1 — Brain Dump

Everyone writes events as fast as possible — no ordering, no judgment.
Events are facts: past tense, concrete, meaningful to the domain.

**Input:** A domain or feature area to explore (e.g., "Flash Session demo-day journey", "Auctions BC internals")
**Process:** Each persona calls out events. No filtering, no sequencing — volume over accuracy.
**Output:** Unordered list of candidate events (expect 15-60 for a single bounded context)

> CritterBids has an existing event vocabulary in `docs/vision/domain-events.md`. For user journey
> workshops, Phase 1 becomes a **verification pass**: walk the journey and confirm the vocabulary
> accounts for everything that happens. Add missing events as discovered.

### Phase 2 — Storytelling

Arrange events into a coherent narrative on the timeline.
Ask: *"What happened first? What does this enable next?"*
Gaps in the story reveal missing events.

**Input:** Unordered event list from Phase 1 (or verified vocabulary for journey workshops)
**Process:** Place events left-to-right on the timeline. Fill gaps: "What happened between X and Y?"
**Output:** Chronologically ordered event timeline with gap markers resolved

### Phase 3 — Storyboarding

Add UI wireframes above the timeline and views below.
Connect them to their triggering commands and resulting events.
This makes the full user journey visible.

**Input:** Ordered event timeline from Phase 2
**Process:** For each event, ask: "What UI triggered this?" (add screen above) and "What does the user see after?" (add view below). Connect with commands.
**Output:** Full storyboard: `UI → Command → Event(s) → View → UI` for the entire flow

### Phase 4 — Identify Slices

Draw vertical cuts through the model — each slice is one complete feature:
`UI → Command → Event(s) → View`
Slices become your work units (stories, tickets, PRs).

**Input:** Complete storyboard from Phase 3
**Process:** Draw vertical lines. Each slice must be independently deliverable and testable.
**Output:** Slice table (see Structured Output Format below)

### Phase 5 — Scenarios (Given/When/Then)

For each slice, write acceptance scenarios:
- **Given**: the events already in the stream (preconditions)
- **When**: the command issued
- **Then**: the new events produced and/or the view state

**Input:** Slice definitions from Phase 4
**Process:** Write happy path first, then edge cases and failure modes per slice.
**Output:** Given/When/Then scenarios per slice

---

## Two Workshop Types

CritterBids uses two complementary workshop formats:

### User Journey Workshop

Walks a cross-cutting scenario (e.g., the Flash Session demo-day spine) end-to-end.
Touches multiple BCs. Produces horizontal coverage — the sequence of handoffs and
integration events across the system.

**Best for:** Validating the integration topology, defining milestone scope, confirming
the event vocabulary covers a complete user scenario.

**Tradeoff:** Does not produce aggregate internals, saga state machine details, or
deep failure/compensation paths within a single BC.

### BC-Focused Workshop

Deep-dives into a single bounded context. Produces vertical depth — aggregate design,
saga state transitions, DCB boundary model details, compensation events, and edge cases.

**Best for:** Implementation-ready designs for a specific BC. Produces the Given/When/Then
scenarios that become test cases.

**Tradeoff:** Does not validate cross-BC integration or end-to-end user experience.

**Recommended sequence:** Run one or two user journey workshops first to establish the
horizontal map, then run BC-focused workshops to fill in vertical depth before implementation.

---

## Structured Output Format for Slices

| # | Slice Name | Command | Events | View | BC | Priority |
|---|-----------|---------|--------|------|----|----------|
| 1 | Start anonymous session | `StartSession` | `ParticipantSessionStarted` | LandingView (display name, bidder ID) | Participants | P0 |
| 2 | Place a bid | `PlaceBid` | `BidPlaced` | BidFeedView (new high bid, bidder) | Auctions | P0 |
| 3 | Close listing with winner | *(scheduled)* | `BiddingClosed`, `ListingSold` | ListingDetailView (sold, winner) | Auctions | P0 |

**Column definitions:**
- **Slice Name**: Human-readable feature name
- **Command**: The command that enters the system (user or system-initiated)
- **Events**: Domain events produced (comma-separated if multiple)
- **View**: The read model or UI state updated after the event
- **BC**: Bounded context that owns this slice (verify against `docs/vision/bounded-contexts.md`)
- **Priority**: P0 = must-have for MVP, P1 = should-have, P2 = nice-to-have

---

## Output Artifacts

- **The Event Model** — the full visual blueprint (primary deliverable)
- **Slice definitions** — vertical feature cuts, each independently deliverable
- **Given/When/Then scenarios** — acceptance criteria per slice
- **API contracts** — command shapes and read model schemas emerge naturally
- **Aggregate/projection sketches** — implementation starting points

---

## Multi-Persona Facilitation

When facilitating a workshop, invoke distinct personas to represent different stakeholder
perspectives. This surfaces conflicts, blind spots, and richer domain understanding than
a single voice would produce.

### CritterBids Personas

Load persona documents from `docs/personas/`. Each persona has a detailed behavioral
profile. See `docs/personas/README.md` for the full roster.

| Persona | File | Role in Workshop |
|---|---|---|
| `@Facilitator` | `facilitator.md` | Leads the workshop, maintains flow, keeps slices small, synthesizes output |
| `@DomainExpert` | `domain-expert.md` | Owns the business language; corrects names, validates against eBay conventions |
| `@Architect` | `architect.md` | Flags BC boundaries, aggregate design, projection feasibility, Critter Stack patterns |
| `@BackendDeveloper` | `backend-developer.md` | Asks "how would we build that?", flags implementation concerns |
| `@FrontendDeveloper` | `frontend-developer.md` | Grounds the model in the React UI; asks what participants see at each step |
| `@QA` | `qa.md` | Stress-tests the model; asks about failures, edge cases, race conditions |
| `@ProductOwner` | `product-owner.md` | Guards scope, prioritizes slices, enforces demo-first constraints |
| `@UX` | `ux.md` | Advocates for participant and seller experience, read model legibility |

### Which Personas Lead Each Phase

| Phase | Primary Voices | Why |
|---|---|---|
| **Brain Dump** | `@Facilitator` + `@DomainExpert` + `@Architect` | Facilitator keeps pace; DomainExpert knows business events; Architect knows technical events |
| **Storytelling** | All eight — `@QA` earns their keep here | QA finds gaps; UX maps events to user moments; everyone contributes to sequencing |
| **Storyboarding** | `@FrontendDeveloper` + `@UX` + `@BackendDeveloper` | Frontend designs screens; UX validates experience; Backend confirms view feasibility |
| **Slicing** | `@Facilitator` + `@ProductOwner` + `@BackendDeveloper` | Facilitator keeps slices crisp; PO prioritizes; Backend validates deliverability |
| **Scenarios** | `@Facilitator` + `@QA` + `@BackendDeveloper` + `@DomainExpert` | QA writes edge cases; Backend validates feasibility; DomainExpert validates accuracy |

### How to Run Multi-Persona Mode

```
[@Facilitator] Let's verify the brain dump. Walk me through what happens
  from the moment a participant scans the QR code.

[@DomainExpert] First thing — they land on the platform and get assigned a
  session. That's ParticipantSessionStarted. They get a display name and a
  hidden credit ceiling. No email, no password.

[@Architect] ParticipantSessionStarted crosses BC boundaries — Auctions needs
  it for bidder validation, Relay needs it for SignalR group enrollment.

[@QA] What if someone scans the QR code twice? Do they get two sessions?
  Or does the second scan rejoin the first?

[@Facilitator] Good edge case. Let's park it — that's a BC-focused workshop
  question for Participants. For this journey, assume one scan, one session.
```

Personas may agree, disagree, and build on each other.
The goal is productive tension — not consensus for its own sake.

---

## CritterBids Integration

### How Workshop Outputs Connect to Existing Artifacts

| Workshop Output | CritterBids Artifact | Location |
|---|---|---|
| **Slices** | Milestone scope documents | `docs/milestones/` |
| **Scenarios (Given/When/Then)** | Test specifications | `tests/` per BC |
| **BC boundary changes** | Update or verify | `docs/vision/bounded-contexts.md` |
| **Event vocabulary changes** | Update or verify | `docs/vision/domain-events.md` |
| **Architectural decisions** | ADR markdown files | `docs/decisions/` |
| **Command / event shapes** | Integration message contracts | `src/CritterBids.Contracts/` |
| **View / read model designs** | Marten or Polecat projections | `src/CritterBids.<BC>/` |

### Existing Documents to Load

| Document | When to load |
|---|---|
| `docs/vision/bounded-contexts.md` | Always — verify BC ownership when assigning slices |
| `docs/vision/domain-events.md` | Phase 1 — the starting vocabulary to verify against |
| `docs/vision/overview.md` | Journey workshops — the demo-day scenario description |
| `docs/personas/README.md` | Session start — decide which personas to activate |

---

## Quick Reference: Common Mistakes to Catch

- Events named as commands: "PlaceBid" is wrong — "BidPlaced" is correct
- No "Event" suffix: "BidPlacedEvent" is wrong — "BidPlaced" is correct
- Missing the "why" behind a command — add a UI wireframe to show the trigger
- Views that can't be derived from the events on the board — you're missing events
- Slices too large to deliver independently — keep slicing
- Scenarios that test infrastructure instead of behavior — focus on domain facts
- Assigning a slice to the wrong BC — check `docs/vision/bounded-contexts.md`
- Skipping the QA voice — edge cases found late are expensive to fix
- Confusing `BiddingClosed` (mechanical) with `ListingSold` (business outcome)
- Treating the Relay BC as owning notification content — it routes, it doesn't originate
