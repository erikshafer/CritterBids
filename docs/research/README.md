# CritterBids Research Notes Index

Research documents are **pre-decision investigation artifacts**: distilled external sources, live
debugging trails, technology evaluations, and escalation packets. They feed ADRs, skills, and
session prompts but are not themselves authoritative — once a research thread resolves, its durable
outcome lives in an ADR (`../decisions/`), a skill (`../skills/`), or code, and the research doc
remains as the record of how the conclusion was reached.

Created at the M8 Bug #2 follow-ups session (2026-06-09), fulfilling the index obligation recorded
in the DCB research doc's original lifecycle note.

| Document | Status | One-liner |
|---|---|---|
| [`dcb-marten-blog-series-research.md`](./dcb-marten-blog-series-research.md) | ✅ Durable reference (distillation live-verified 2026-06-09) | Babu Annamalai's 4-part "DCB in Marten" series distilled: the aggregate trap, the API surface, the canonical Wolverine.HTTP endpoint, production traps (tag governance, hard-cap mechanics, global-lock failure mode). |
| [`bug2-bidplaced-delivery-investigation.md`](./bug2-bidplaced-delivery-investigation.md) | ✅ Resolved (historical trail) | The full Bug #2 debugging record split out of the DCB doc: eight falsified publish-side experiments, the §5.5 root-cause resolution, and post-resolution notes on the candidate fixes. The methodology it taught is codified in `../skills/message-flow-diagnosis/SKILL.md`. |
| [`jasperfx-escalation-bidplaced-cross-bc-delivery.md`](./jasperfx-escalation-bidplaced-cross-bc-delivery.md) | ✅ Root-caused (fix merged in PR #90) | The authoritative Bug #2 report: Wolverine ≤6.5.x Separated-mode single-saga chains suppress the sticky-handler fan-out; mechanism, live proof, fix options, prior art (#3041/#3042). |
| [`wolverine-upstream-saga-sticky-separation-handoff.md`](./wolverine-upstream-saga-sticky-separation-handoff.md) | ⏳ Ready to execute | Self-contained agent work order for the upstream Wolverine fix PR (SagaChain single-saga sticky separation), with repro, test plan, prior art, and PR mechanics. |
| [`frontend-stack-research.md`](./frontend-stack-research.md) | ✅ Consumed (→ ADR 013) | RF-1 frontend stack evaluation that fed the ADR 013 library composition. |
| [`auction-ux-research.md`](./auction-ux-research.md) | ✅ Consumed (→ M8 milestone) | RF-2 auction UX patterns (eBay conventions, live-bidding affordances) feeding the M8 bidder surfaces. |
| [`grpc-opportunities-research.md`](./grpc-opportunities-research.md) | 📚 Parked | Where gRPC could fit CritterBids if ever warranted; no adoption decision taken. |
| [`learnings-file-scope.md`](./learnings-file-scope.md) | ✅ Resolved as "skip" | Whether to maintain a LEARNINGS.md file; declined — skills and retros own that surface. |
| [`methodology-log.md`](./methodology-log.md) | 📜 Living log | Running notes on the project's methodology experiments (SDD/NDD, session loop evolution). |

## Conventions

- One document per investigation thread; prefer updating in place while the thread is live.
- When a thread resolves, mark the status here and in the doc header, and point at the durable
  artifact that absorbed the outcome. Split long live-debugging trails out of distillation docs
  (the DCB/Bug #2 split is the precedent) so references stay stable.
- Escalation packets destined for JasperFx keep the full evidence chain — they double as the
  upstream issue/PR text.
