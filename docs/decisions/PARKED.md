# CritterBids Parked Decisions

Methodology and architectural decisions deliberately deferred with explicit triggers. A parked decision is *not* an undecided one - it has been considered, and the act of deferral is the disposition. Each entry records what is parked, what condition triggers reopening, and the candidate scope of the future session.

This ledger is append-only. Parked items move out of the **Active** section (to an ADR, decision note, or implementation slice) when their trigger fires; the closed entry gets a brief outcome line and stays in the **Closed** section for traceability.

Distinct from [`docs/workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md), which catalogues domain-modeling questions surfaced during workshop sessions. This file holds methodology-grade and architecture-grade parked decisions.

---

## Active

### P-001 - Operations runbook / SRE-style docs

**Source:** [`docs/prompts/foundation/foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §7.5. Resolved as parked in foundation-refresh Phase 4 Q5 (2026-04-27).

**Trigger.** When the first production-leaning deployment is scheduled. Specifically: when a CritterBids deployment is planned for any environment beyond a developer's local Aspire orchestration - Hetzner VPS, conference-demo cloud instance, public preview, internal staging environment, or any public-facing host.

**Candidate scope.** Hetzner VPS topology and provisioning; health-check endpoints and uptime probes; alerting policy and routing; incident playbooks; deploy and rollback procedures; secret rotation; backup and restore; runtime observability (logging, metrics, traces).

**Why parked.** Deployment and operations are referenced across ADR-006 (Aspire as local-orchestration path) and ADR-011 (All-Marten Pivot, which has runtime implications) but no unified ops doc exists. Authoring a runbook now risks bit-rot before its first run; deferring with a clear trigger lets the runbook be authored against actual deployment constraints (chosen environment, observability stack, alerting tooling).

### P-002 - Demo-script runbook

**Source:** [`docs/prompts/foundation/foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §7.4. Resolved as parked in foundation-refresh Phase 4 Q4 (2026-04-27).

**Trigger.** When the first conference talk is scheduled. Specifically: when CritterBids is committed to a specific conference event with date, venue, presenter, and talk length, the trigger fires and a runbook session is opened against the actual constraints of that talk.

**Candidate scope.** Setup checklist (Aspire orchestration warm-up, demo data seeding, browser windows pre-positioned); demo runtime estimate broken down by Moment from Narrative 001; talking-point alignment with each Moment's "Why this matters to the bidder" block; contingency plan for live-demo failures (network drop, SignalR disconnect, Wolverine outbox stall); audience-reset points (where the presenter can pause for questions without losing the demo's narrative thread); post-demo Q&A talking points anchored to W001's "All Parked Questions" table.

**Why parked.** The Flash demo journey is dramatized in [Narrative 001](../narratives/001-bidder-wins-flash-auction.md) (single-bidder happy path) and walked at workshop grain in [W001](../workshops/001-flash-session-demo-day-journey.md) (full Tier 0-9 storyboard with `## Cast` and `## Setting` blocks from foundation-refresh Phase 3 Item 3). A presenter-perspective runbook adds a third lens but has no immediate consumer - no conference talk is currently scheduled. Premature authoring runs the risk of bit-rot before its first run; deferring with a clear trigger lets the runbook be authored against actual presentation constraints (talk length, audience composition, stage tooling, demo-data freshness).

**Implementation options when triggered.** Presenter overlay on Narrative 001 (sibling file under `docs/narratives/`, or appended section, carrying stage directions per Moment) versus standalone runbook (`docs/demo/flash-session-runbook.md` in a new `docs/demo/` directory, workflow-grain content with setup, runtime, contingency, Q&A). The choice depends on conference constraints: presenter overlay if the talk follows Narrative 001's bidder perspective end-to-end; standalone runbook if the talk's structure diverges (operator-perspective demo, mixed-perspective talk, talk shorter than narrative 001 dramatizes).

---

## Closed

(none yet)

---

## Document history

- **v0.1** (2026-04-27): Authored at foundation-refresh Phase 4 close as the parked-items ledger. P-001 (Operations runbook) and P-002 (Demo-script runbook) land in the same Phase 4 PR.
