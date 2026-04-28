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

---

## Closed

(none yet)

---

## Document history

- **v0.1** (2026-04-27): Authored at foundation-refresh Phase 4 Q5 close as the parked-items ledger. P-001 (Operations runbook) is the first entry. Phase 4 Q4 (Demo-script runbook) lands as P-002 in the same Phase 4 PR.
