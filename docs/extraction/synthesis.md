# Synthesis

A cold-readable overview of CritterBids as it exists today. Every claim here is restated from another artifact in `docs/extraction/` — no new facts are introduced. Cross-references point to the dossier, workflow trace, glossary entry, or gap-register row that holds the cited evidence.

This document is the entry point for a reader who has 15 minutes and wants to understand the system.

---

## What CritterBids is

CritterBids is a **.NET 10 modular monolith on the Critter Stack** (Wolverine + Marten) that auctions things. It is open-source, conference-demo software, structured for AI-augmented learning of distributed-architecture patterns within a single deployable. PostgreSQL is the only storage engine in lived code (`bcs/*.md`); RabbitMQ is the only message transport between BC modules (`bcs/auctions.md`); Aspire is the only local-orchestration path (`docs/extraction/README.md` Method §1 derived from `CLAUDE.md:48`).

The system is **modeled after eBay's auction conventions**, not Sotheby's: the public-facing primary noun is "listing," not "lot"; closing produces `ListingSold` or `ListingPassed`, not "hammer fell" or "lot passed"; the platform charges a Final Value Fee to the seller (`glossary.md` Final Value Fee).

## The eight bounded contexts

| BC | Maturity | One-line purpose | Dossier |
|---|---|---|---|
| **Participants** | Partial | Anonymous participant sessions + the one-time seller registration gate. | `bcs/participants.md` |
| **Selling** | Implemented | Self-service seller listing: draft → submit → approve → publish; withdraw. | `bcs/selling.md` |
| **Auctions** | Implemented | Bidding mechanics, DCB, Auction Closing saga, Proxy Bid Manager saga, Buy It Now, sessions. | `bcs/auctions.md` |
| **Listings** | Partial | Public browsable catalog projection across Selling, Auctions, and Settlement. | `bcs/listings.md` |
| **Settlement** | Implemented | Settlement saga, reserve check, fee calc, seller payout, `PendingSettlement` projection. | `bcs/settlement.md` |
| **Obligations** | Planned-only | Post-sale buyer-payment / seller-fulfillment obligations and reminder chain. | `bcs/obligations.md` |
| **Relay** | Planned-only | SignalR push and notification routing. | `bcs/relay.md` |
| **Operations** | Planned-only | Staff dashboard, cross-BC read models, demo reset. | `bcs/operations.md` |

The **five Implemented or Partial BCs deliver the lived bidder-experience arc** from QR-scan-to-session-start through settlement-completed (`workflows/timed-listing-close.md`, `workflows/buy-it-now.md`). The **three Planned-only BCs deliver the post-sale story and the operator dashboards** that the vision doc describes — none of that story is wired in code (`gaps-and-drift.md` Class 2 entries D2.01-D2.04).

## The lived bidder-experience arc

Five business processes are documented as cross-BC traces. Three of them are fully wired in code (Implemented). Three are partial, scaffolded, or absent (per the workflow's per-step maturity tags).

| Workflow | Maturity | What it spans | Trace |
|---|---|---|---|
| Publish → Bidding Open | Implemented | Selling publishes a listing; Auctions starts a stream and emits `BiddingOpened`; Listings updates the catalog. | `workflows/publish-to-bidding-open.md` |
| Timed Listing Close | Implemented through `BiddingClosed`; downstream Settlement Implemented. | Auctions saga schedules a `CloseAuction` command; close evaluation emits `ListingSold` or `ListingPassed`; Settlement starts. | `workflows/timed-listing-close.md` |
| Buy It Now | Implemented through `BuyItNowPurchased`; Settlement Implemented for the BIN source overload. | `BuyNow` command bypasses the bidding flow; saga terminates with the BIN-source signal. | `workflows/buy-it-now.md` |
| Proxy Bidding | Implemented (skeleton in code) | Proxy Bid Manager saga emits human-like `PlaceBid` commands up to a registered maximum. Composite-key correlation via dispatcher-bridge. | `workflows/proxy-bidding.md` (OQ-P2-01 flags `IsProxy` plumbing gap) |
| Post-Sale Obligations | **Planned-only** | Settlement emits `SettlementCompleted` → Obligations would consume → reminders + carrier integration. The Obligations BC does not exist. | `workflows/post-sale-obligations.md` |
| Flash Session | Partial | Auctions session-start fan-out wired; the sale-floor demo UX is not wired. | `workflows/flash-session.md` |

The lived integration topology (RabbitMQ queues + handlers, all per-BC) is mapped in each dossier's "Cross-BC integrations" section and consolidated in the M3 and M5 retros (`docs/retrospectives/M3-auctions-bc-retrospective.md`, `docs/retrospectives/M5-retrospective.md`).

## Where the system is, today

A reader who clones the repo and runs `dotnet run --project src/CritterBids.AppHost` gets:

- **A running modular monolith** (`CritterBids.Api`) with five BCs registered, each contributing types via `services.ConfigureMarten()`, a single shared primary Marten store per ADR 009/011 (`lessons.md` §1).
- **An end-to-end bidder happy path**: register as seller → draft → submit → approve → publish → bid → close → settlement → seller payout (route only; no payment processor) (`workflows/timed-listing-close.md`, `bcs/settlement.md`).
- **A Buy It Now alternative path** with atomic BIN-removal semantics (`workflows/buy-it-now.md`, `bcs/auctions.md`).
- **A proxy-bidding mechanic** skeleton with composite-key saga correlation (`workflows/proxy-bidding.md`).
- **A public catalog projection** that updates across Selling-, Auctions-, and Settlement-sourced events via Path A sibling handlers (ADR 014; `bcs/listings.md`).
- **A staff dashboard** that does not exist (Operations is Planned-only; `bcs/operations.md`).
- **Real-time push** that does not exist (Relay is Planned-only; `bcs/relay.md`).
- **A post-sale obligations chain** that does not exist (Obligations is Planned-only; `bcs/obligations.md`).
- **No authentication**: `[AllowAnonymous]` is the project's intentional stance through M6 (`CLAUDE.md:104`; conflict with copilot-instructions.md noted in `gaps-and-drift.md` D1.02).

The runtime topology is documented in the M3 and M5 retros' "Cross-BC Integration Map" sections and the per-BC dossier "Cross-BC integrations" sections.

## Recurring architectural patterns

These patterns appear two or more times across the lived BCs and are documented in the dossiers, glossary, or lessons:

1. **Stream-existence pre-query for idempotency** — `ListingPublishedHandler` and `SessionStartedHandler` both check whether the listing's stream exists before appending. Distinct from DCB, which gates per-event consistency once the stream exists. (`bcs/auctions.md` Notable internal conventions; `glossary.md` "Stream-existence pre-query")
2. **Tolerant-upsert + status-preservation in projections** — `CatalogListingView` sibling handlers and `PendingSettlement` projection both adopt the pattern: load-or-create, guard on pre-state, additive field write. Formalized as ADR 014 Path A. (`bcs/listings.md`; `bcs/settlement.md`; `lessons.md` §5)
3. **Saga `NotFound` absorber on terminal paths** — `AuctionClosingSaga`, `ProxyBidManagerSaga`, and `SettlementSaga` all carry `public static NotFound(X) => new()` companions to absorb post-`MarkCompleted` redeliveries. (`lessons.md` §3; `bcs/auctions.md`; `bcs/settlement.md`)
4. **`MultipleHandlerBehavior.Separated` + `SendMessageAndWaitAsync` testing idiom** — set in `Program.cs:20`; required dispatch method for all cross-BC tests. (`bcs/auctions.md`; `lessons.md` §4)
5. **Deterministic UUID v5 for natural-key streams; v7 for everything else** — `SettlementId(listingId)` and `ProxyBidManagerSagaId(listingId, bidderId)` use v5; new listings, new sessions use `Guid.CreateVersion7()`. (`glossary.md` UUID v5 / UUID v7; ADR 007 amendments)
6. **Composite-key saga correlation via dispatcher-bridge** — Wolverine's `[SagaIdentityFrom]` resolves only Guid by name, so the Proxy Bid Manager uses `ProxyBidDispatchHandler` to query active sagas and emit per-saga wrapped events. (`workflows/proxy-bidding.md`; `lessons.md` §6)
7. **Cross-BC contract promotion in same slice as first consumer** — `ParticipantSessionStarted` was promoted from `CritterBids.Participants.Features.*` to `CritterBids.Contracts.Participants.*` in M5-S5 when Settlement became the first cross-BC consumer. (`bcs/settlement.md`; `docs/retrospectives/M5-retrospective.md` M5-D2)
8. **Duplicate per-BC read models from shared event sources** — `ParticipantCreditCeiling` in Auctions and `BidderCreditView` in Settlement both project from `ParticipantSessionStarted`, with diverged schemas appropriate to each BC's needs. (`lessons.md` §5)
9. **Meaningful event absence as audit signal** — Settlement's financial event stream omits `ReserveCheckCompleted` on BIN settlements; absence is the "this was a BIN" signal. (`bcs/settlement.md`; `glossary.md` "Meaningful event absence")
10. **Outcome events as terminal close signal, never `BiddingClosed`** — the mechanical close emits `BiddingClosed` for completeness, but downstream BCs subscribe to `ListingSold` / `ListingPassed` / `BuyItNowPurchased`. (`bcs/auctions.md`; `lessons.md` §14)

These patterns are observable in source code today and are the practical vocabulary of working on the system.

## House conventions (the rules that are actually followed)

The conventions in `CLAUDE.md` that survive the doc-vs-code drift check (see `gaps-and-drift.md`):

- `sealed record` for all commands, events, queries, read models (`glossary.md` `sealed record`)
- `IReadOnlyList<T>`, never `List<T>` on records or aggregates
- Handlers return events/messages — never call `session.Store()` directly
- Saga terminal paths call `MarkCompleted()` and have a `NotFound` absorber companion
- `opts.Policies.AutoApplyTransactions()` in `Program.cs:18` exactly once — not per BC (`gaps-and-drift.md` D1.04 flags the copilot-instructions divergence)
- Integration events via `OutgoingMessages` return — never `IMessageBus.PublishAsync` from handler bodies
- `bus.ScheduleAsync()` is the only justified `IMessageBus` use in handlers (saga scheduled close uses this)
- No "Event" suffix on domain event type names
- No "paddle" anywhere — bidder identifier is `BidderId`
- `[AllowAnonymous]` on all endpoints through M6 (`CLAUDE.md:104`)

## Where intent and reality diverge

Three classes of drift are catalogued in `gaps-and-drift.md`:

- **Class 1 — Doc-vs-code (13 entries)**: `.github/copilot-instructions.md` divergences (storage, auth, UUID, transactions); vision-doc present-tense framing of unbuilt BCs; small surface-area divergences in test count and naming.
- **Class 2 — Declared-but-not-built (14 entries)**: features named in vision docs or W003 narratives with no `src/` realization — Obligations BC, Relay BC, Operations BC, watchlist, search index, payment processor, demo reset, etc.
- **Class 3 — Declared-but-not-wired (9 entries)**: types or routes exist in code but have no consumer or no producer — `SellerPayoutIssued` published but no Relay consumer; `PaymentFailed` published but no Operations consumer; `BidRejected` event type exists but no subscriber; etc.

The drift is **a register of intent vs reality, not a backlog**. No remediation is prescribed in this corpus.

## The two open questions

`OPEN-QUESTIONS.md` currently holds one entry:

- **OQ-P2-01** — `IsProxy` flag plumbing on saga-emitted `PlaceBid`. The `BidPlaced` contract docstring describes the proxy-bid integration, but the field is hardcoded `false` in `PlaceBidHandler.AcceptanceEvents` and absent from `PlaceBid` itself. Either the docstring is aspirational or the wiring is planned but not yet present.

No other unresolvable ambiguities surfaced through the six extraction phases.

## Reading order for newcomers

1. **This file** for the 15-minute overview.
2. **`bcs/auctions.md`** as the richest single dossier — it touches DCB, two sagas, BIN, sessions, cross-BC consumers and producers.
3. **`workflows/timed-listing-close.md`** for the canonical cross-BC end-to-end flow.
4. **`glossary.md`** as a reference while reading the dossiers.
5. **`gaps-and-drift.md`** to calibrate what's documented but not built.
6. **`lessons.md`** for the storage-arc story (§1) and the methodology patterns (§3-§4, §10-§11).

## What this corpus is not

- It is not a backlog or work-tracking document.
- It is not a rebuild plan or successor-system specification.
- It does not modify, re-author, or "correct" any `src/` source or pre-existing doc.
- It does not infer behavior for Planned-only BCs beyond what `docs/vision/` already says.
- Only `lessons.md` carries judgment. Every other artifact is descriptive.
