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

## Adjunct Patterns

Beyond the four core building blocks, CritterBids' workshops and lived code surface three named event-modeling patterns. Naming them here lets workshop prose, narrative authoring, and ADRs refer to each pattern by its published-literature name rather than re-deriving the shape each time.

Sources: Adam Dymitruk (Adaptech Group, the core method), Filip Klefter (translation-decision events), and Anders Bruun Olsen (temporal-automation slice pattern, configuration-as-events).

### Klefter Translation-Decision Events

When a slice coordinates with an external system AND a decision is made locally based on the external input, the local decision is captured as a first-class event in the BC's stream. Names the BC's authority over the decision even though the input came from outside; the event is the audit trail of "I asked X, got Y, decided Z."

**Pattern signal:** an outbound query whose result the BC commits as a local event before any further processing.

**CritterBids example:** the Auctions BC emits `ReserveMet` as a Klefter translation-decision. The DCB reads the listing's reserve value (via tag query against the listing stream) and decides whether the new high bid meets the reserve; the decision lands as `ReserveMet`, which the Relay BC consumes as a real-time UX signal. Settlement BC's authoritative `ReserveCheckCompleted` is a separate event with different authority - same source data, different role. See [W002 §"Ubiquitous Language"](../../workshops/002-auctions-bc-deep-dive.md#ubiquitous-language) and [W003 §"Ubiquitous Language"](../../workshops/003-settlement-bc-deep-dive.md#ubiquitous-language) for the cross-BC vocabulary.

A second candidate in CritterBids: a future Settlement-to-Participants credit-ceiling check would commit the result as a local Settlement event (`CreditCheckCompleted` or similar) before the workflow proceeds, making the input visible in the audit log without coupling Settlement to Participants' internal projection shape.

### Bruun Temporal-Automation Slice Pattern

A slice whose trigger is the passage of time, not an incoming domain event. The slice fires when a clock condition is met (`now() >= scheduledFor`) on a row in a todo-list read model. Boards render the pattern with two distinguishing marks: a clock-rewind glyph on the gear (automation) sticky, and an asterisk suffix on the read model's name (e.g., `OffersAwaitingExpiry*`, `AuctionsAwaitingClose*`).

**Pattern signal:** an automation whose trigger is clock state, consuming a todo-list read model whose rows self-remove when the work completes.

**CritterBids example:** the Auction Closing Saga's scheduled `CloseAuction` timer is a temporal-automation slice. The saga schedules the timer at `BiddingOpened`; when the timer fires, the saga reads the listing's current state and resolves to `ListingSold` or `ListingPassed`. A candidate todo-list projection `AuctionsAwaitingClose*` would carry rows added on `BiddingOpened` and removed on resolution. The asterisk convention is preserved in narratives per [`docs/narratives/README.md`](../../narratives/README.md) §"Notation conventions"; this section names the underlying pattern.

### Configuration-as-Events (Bruun)

Operator-tunable policy parameters represented as events on a singleton stream rather than rows in a settings table. Each configuration change is an event; the current policy is the latest event's payload. Provides audit trail, version history, and natural integration with event-driven downstream consumers.

**Pattern signal:** policy that needs an audit trail and version history, where downstream consumers should react to changes rather than periodically re-read a settings table.

**CritterBids candidate:** the Auction Closing Saga's `triggerWindow`, `extension`, and `maxDuration` parameters are constants today. If the project decides to make them operator-tunable (post-MVP), they would land as `AuctionPolicyConfigured` events on a singleton stream. The Auctions BC's `BiddingOpened` payload would carry the policy version governing the listing's lifecycle, so a mid-listing policy change would not retroactively affect in-flight auctions.

This section names patterns; it does not commit CritterBids to refactor existing code. Naming makes the model legible when the project encounters these patterns elsewhere or when a future ADR proposes adopting one for a specific BC.

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
