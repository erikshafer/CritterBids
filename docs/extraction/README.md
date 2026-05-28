# CritterBids Business Extraction

A source-cited descriptive record of CritterBids' bounded contexts, business processes, and domain vocabulary as the system exists today; a factual register of every gap and drift between intent and reality; and a single lessons document mined from the project's retrospectives and ADR history. Every bounded context and process is tagged by implementation maturity.

Driving prompt: [`docs/prompts/foundation/business-extraction-handoff.md`](../prompts/foundation/business-extraction-handoff.md).

## Method

1. **Code is source of truth.** When a vision doc, ADR, `CLAUDE.md`, or `.github/copilot-instructions.md` disagrees with `src/` or `tests/`, the code wins. Divergences are recorded in `gaps-and-drift.md`, never silently reconciled.
2. **Two registers, never mixed.** Dossiers, process traces, glossary, gap register, and synthesis are **descriptive** — facts only. Lessons is the **evaluative** artifact — the sole place judgment lives.
3. **Every BC, process, and notable capability carries a maturity tag.** A reader skimming any artifact can tell working behavior from declared intent without cross-referencing `src/`.
4. **Every structural claim cites a source path.** Behavioral claims cite the test that proves them.

## Maturity taxonomy

| Tag | Definition |
|---|---|
| **Implemented** | A `src/CritterBids.<BC>` project exists with aggregates/handlers and is registered in the API host; behavior is exercised by tests. |
| **Partial** | The project exists and runs, but a capability the vision docs attribute to it is absent or stubbed. |
| **Scaffolded** | A project or folder exists but holds little more than registration and stubs; minimal real behavior. |
| **Planned-only** | Described in vision docs with no corresponding `src/` project. |

Capability-level tags within an otherwise-Implemented BC are encouraged (a BC may be Implemented overall while a specific saga path is Partial).

## Bounded-context status

Verified against `src/` and `tests/`. Evidence cited inline.

| BC | Maturity | One-line purpose | Project |
|---|---|---|---|
| Participants | **Partial** | Anonymous participant sessions and the one-time seller registration gate. | `src/CritterBids.Participants` — 6 cs files; aggregate, `StartParticipantSession`, `RegisterAsSeller` features; no `SellerProfile` aggregate, no session-end path. |
| Selling | **Implemented** | Self-service seller listing draft → submit → approve → publish; withdraw. | `src/CritterBids.Selling` — 20 cs files; `SellerListing` aggregate, validator, registration projection, withdraw command. |
| Auctions | **Implemented** | Bidding mechanics, DCB, Auction Closing saga, Proxy Bid Manager saga, Buy It Now, sessions. | `src/CritterBids.Auctions` — 37 cs files; two sagas, DCB boundary model, session aggregate. |
| Listings | **Partial** | Public browsable catalog projection across Selling, Auctions, and Settlement. | `src/CritterBids.Listings` — 8 cs files; `CatalogListingView` with four sibling handlers, catalog endpoints; no `LotWatchAdded` / `LotWatchRemoved`, no watchlist, no search index. |
| Settlement | **Implemented** | Settlement saga, reserve check, fee calc, seller payout, `PendingSettlement` projection. | `src/CritterBids.Settlement` — 25 cs files; saga, financial event stream, bidder credit view. |
| Obligations | **Planned-only** | Post-sale obligations and reminder chain. | No `src/CritterBids.Obligations` project. Declared in `docs/vision/bounded-contexts.md` lines 139–162. |
| Relay | **Planned-only** | SignalR push and notification routing. | No `src/CritterBids.Relay` project. Declared in `docs/vision/bounded-contexts.md` lines 165–186. |
| Operations | **Planned-only** | Staff dashboard, cross-BC read models, demo reset. | No `src/CritterBids.Operations` project. Declared in `docs/vision/bounded-contexts.md` lines 189–211. |

The Auctions BC is the only one whose `src/` layout matches the "flat vertical-slice" pattern named in the handoff prompt across the board. Participants uses `Features/<feature>/` subfolders; Listings is partially `Features/` (one folder, six flat). Selling and Settlement are flat. This layout drift is recorded; it is not evaluated here.

## Artifact status

| Artifact | Status | Notes |
|---|---|---|
| [`README.md`](./README.md) | Phase 4 gate | Updated at every gate. |
| [`bcs/`](./bcs/) | Complete | All 8 dossiers written: 5 Implemented/Partial + 3 Planned-only. |
| [`workflows/`](./workflows/) | Complete | 6 cross-BC process traces: publish-to-bidding-open, timed-listing-close, buy-it-now, proxy-bidding, post-sale-obligations, flash-session. |
| [`glossary.md`](./glossary.md) | Complete | ~60 terms swept from vision docs, code, and house naming rules; drift cross-referenced to `gaps-and-drift.md`. |
| [`gaps-and-drift.md`](./gaps-and-drift.md) | Complete | 13 doc-vs-code, 14 declared-but-not-built, 9 declared-but-not-wired entries. |
| [`lessons.md`](./lessons.md) | Stub | Populated in Phase 5 — the only evaluative artifact. |
| [`synthesis.md`](./synthesis.md) | Stub | Authored in Phase 6. |
| [`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md) | Stub | Appended to as the extraction proceeds. |

## Navigation

- Bounded-context dossiers: `bcs/<bc>.md`
- Business process traces: `workflows/<process>.md`
- Vocabulary: [`glossary.md`](./glossary.md)
- Doc-vs-code, declared-but-not-built, declared-but-not-wired: [`gaps-and-drift.md`](./gaps-and-drift.md)
- Generalizable lessons mined from retros and ADRs: [`lessons.md`](./lessons.md)
- Cold-readable synthesis: [`synthesis.md`](./synthesis.md)
- Escalations: [`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md)

## Scope and non-scope

- **In scope:** documenting what exists, citing where it lives, registering drift as a fact.
- **Out of scope:** modifying `src/`, editing pre-existing docs to repair drift, authoring ADRs, designing or referencing the downstream rebuild methodology, pulling CritterSupply or CritterCab into the analysis, inferring behavior for Planned-only BCs beyond what `docs/vision/` declares.
