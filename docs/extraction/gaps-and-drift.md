# Gaps and Drift

Factual register of every observed divergence between intent (vision docs, ADRs, `.github/copilot-instructions.md`, `CLAUDE.md`, `Contracts`) and reality (`src/`, `tests/`, RabbitMQ wiring in `Program.cs`).

Three classes. Every entry: the claim, the source of the claim, the reality, the source of the reality, the classification. No editorializing beyond classification.

---

## Class 1 — Doc-vs-code drift

Where an authoritative-looking document contradicts code or a superseding ADR.

### D1.01 — Storage table claims Polecat / SQL Server for three BCs

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| "Polecat (SQL Server) for Participants, Settlement, Operations BCs" | `.github/copilot-instructions.md:11`; storage table lines 32-41 | All five existing BC projects use Marten/PostgreSQL exclusively. No Polecat package is referenced in `src/CritterBids.Participants/*.csproj`, `src/CritterBids.Settlement/*.csproj`, or anywhere else. Module registration is `services.ConfigureMarten()` for both Participants (`ParticipantsModule.cs:15`) and Settlement (`SettlementsModule.cs`). | ADR 011 (all-Marten pivot, supersedes ADR 003); `src/CritterBids.Api/Program.cs:180-203` shows single `AddMarten` call | Doc-vs-code drift |

### D1.02 — `[Authorize]` claimed as universal

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| "`[Authorize]` on all non-auth endpoints" | `.github/copilot-instructions.md:23` | All HTTP endpoints are `[AllowAnonymous]` through M6. This is the intentional project stance, not a temporary override. | `CLAUDE.md:102-103` ("real authentication is deferred to M6; this is the intentional project stance, not a temporary override. The `[Authorize]` convention resumes at M6."); `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs`, `src/CritterBids.Selling/CreateDraftListingHandler.cs`, `src/CritterBids.Listings/Features/Catalog/CatalogEndpoints.cs` all show `[AllowAnonymous]` | Doc-vs-code drift |

### D1.03 — UUID v5 claimed as the universal stream-ID strategy

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| "UUID v5 stream IDs with BC-specific namespace prefixes" | `.github/copilot-instructions.md:26` | UUID v7 (`Guid.CreateVersion7()`) is the primary stream-ID strategy when no natural business key exists. UUID v5 is used for two deterministic-ID cases: the Settlement saga id (`SettlementId(listingId)`) and the Proxy Bid Manager saga id (`UuidV5(ns, $"{ListingId}:{BidderId}")`). Most stream creations (`Participant`, `SellerListing`, `Session`, bid ids) use UUID v7. | ADR 007 (UUID v7 primary; v5 when natural business key enables determinism); `CLAUDE.md:106-110`; `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs:47`; `src/CritterBids.Selling/CreateDraftListingHandler.cs:78`; `src/CritterBids.Auctions/CreateSession.cs:31-47` | Doc-vs-code drift |

### D1.04 — `AutoApplyTransactions` claimed as per-BC

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| "`opts.Policies.AutoApplyTransactions()` in every BC's Marten/Polecat config" | `.github/copilot-instructions.md:22` | Configured exactly once globally inside `UseWolverine` in the API host. No BC's `Configure*Module()` extension touches the transaction policy. | `CLAUDE.md:104` ("in `UseWolverine()` in `Program.cs` — not inside BC `ConfigureMarten()` calls"); `src/CritterBids.Api/Program.cs:184` (single `opts.Policies.AutoApplyTransactions()` call) | Doc-vs-code drift |

### D1.05 — Vision doc describes Obligations BC in present tense

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Obligations BC described with confident present-tense prose: aggregates, projections, events, saga, integration points | `docs/vision/bounded-contexts.md:139-162`; `docs/vision/domain-events.md:97-106` ("Obligations" event table with 8 events) | No `src/CritterBids.Obligations` project exists. No corresponding queue routes in `Program.cs`. No CLR types for any of the 8 declared events. | Phase 0 inventory (`bcs/obligations.md`); `Get-ChildItem src/` shows no Obligations project | Doc-vs-code drift (present-tense framing in vision doc despite Planned-only maturity) |

### D1.06 — Vision doc describes Relay BC in present tense

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Relay BC described with confident present-tense prose: SignalR push, notification routing, "pure consumer" stance | `docs/vision/bounded-contexts.md:165-186`; `docs/vision/domain-events.md:110-112` | No `src/CritterBids.Relay` project exists. The queue routes `relay-auctions-events` and `relay-settlement-events` exist in `Program.cs:107-109, 155-156` but no `ListenToRabbitQueue` consumer is wired. | Phase 0 inventory (`bcs/relay.md`) | Doc-vs-code drift (present-tense framing in vision doc despite Planned-only maturity) |

### D1.07 — Vision doc describes Operations BC in present tense

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Operations BC described with confident present-tense prose: staff dashboard, cross-BC read models, demo reset capability | `docs/vision/bounded-contexts.md:189-211`; `docs/vision/domain-events.md:116-120` | No `src/CritterBids.Operations` project exists. The queue route `operations-settlement-events` exists in `Program.cs:162-163` but no consumer is wired. | Phase 0 inventory (`bcs/operations.md`) | Doc-vs-code drift (present-tense framing in vision doc despite Planned-only maturity) |

### D1.08 — `BidderId` claimed as the cross-BC participant identifier

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| "Bidder IDs are the participant's identifier across all BCs" | `docs/vision/bounded-contexts.md:25` | Cross-BC consumers route on `ParticipantId` (Guid), not `BidderId` (string). `BidderId` is a display-only string. | `src/CritterBids.Settlement/BidderCreditView.cs` (keyed on `ParticipantId`); `src/CritterBids.Auctions/ParticipantCreditCeiling.cs` (keyed on `ParticipantId`); `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs:33-37` (XML doc explicitly states `BidderId` is for display) | Doc-vs-code drift |

### D1.09 — `SellerProfile` aggregate declared but not present

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Participants BC owns a `SellerProfile` aggregate distinct from `Participant` | `docs/vision/bounded-contexts.md:18` | Only the `Participant` aggregate exists. The seller-registered flag is a `bool IsRegisteredSeller` property on `Participant`. | `src/CritterBids.Participants/Participant.cs:14-16, 24-27`; absence of any `SellerProfile.cs` in repo | Doc-vs-code drift |

### D1.10 — `FeePercentage` claimed to come from platform config

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `FeePercentage` "captured from platform config at publish time and fixed for the life of the listing" | `docs/vision/domain-events.md:34` | Hardcoded literal `0.10m` in the submit handler, with a comment naming it as an M5 placeholder. No platform-config seam exists. The contract event still carries the field. | `src/CritterBids.Selling/SubmitListingHandler.cs:70` ("M5 placeholder — no fee engine exists yet") | Doc-vs-code drift |

### D1.11 — W003 Phase 1 Part 6 specified the wrong UUID v5 namespace for `SettlementId`

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| W003 Phase 1 Part 6 specified `AuctionsNamespace` for SettlementId derivation | W003 Phase 1 Part 6 (workshop doc; corrected reference in `bcs/settlement.md`) | Code uses `SettlementsIdentityNamespaces.SettlementId`. The workshop-doc drift was caught and corrected in code at M5-S4. | `src/CritterBids.Settlement/SettlementsIdentityHelpers.cs`; `glossary.md` SettlementId entry | Doc-vs-code drift (workshop-doc drift, corrected in code) |

### D1.12 — M5-S3 milestone doc §2 omits `ListingPassed` on `settlement-auctions-events`

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| M5-S3 milestone §2 lists `ListingSold` and `BuyItNowPurchased` on the `settlement-auctions-events` queue, but not `ListingPassed` | M5-S3 milestone doc §2 | Code routes all three: `ListingSold`, `BuyItNowPurchased`, AND `ListingPassed` to `settlement-auctions-events` (`Program.cs:114-120`) | `src/CritterBids.Api/Program.cs:114-120`; `bcs/settlement.md` | Doc-vs-code drift (code extends the documented contract) |

### D1.13 — `BidPlaced` doc says M4 wires `IsProxy=true` from the proxy saga

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `BidPlaced` contract docstring states "M4 wires the Proxy Bid Manager saga to set `IsProxy=true` on auto-bids" | `src/CritterBids.Contracts/Auctions/BidPlaced.cs` docstring (per OPEN-QUESTIONS.md entry OQ-P2-01) | `PlaceBid` command has no `IsProxy` field; `PlaceBidHandler.AcceptanceEvents` hardcodes `IsProxy: false` on the emitted `BidPlaced`. The saga emits a `PlaceBid` command in R5b but no plumbing sets `IsProxy=true` on the resulting `BidPlaced`. | `src/CritterBids.Contracts/Auctions/PlaceBid.cs`; `src/CritterBids.Auctions/PlaceBidHandler.cs:116`; `OPEN-QUESTIONS.md` OQ-P2-01 | Doc-vs-code drift |

---

## Class 2 — Declared-but-not-built

Capabilities or whole BCs the docs describe that have no code.

### B2.01 — Obligations BC

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| BC scope, aggregates, projections, saga, integration events | `docs/vision/bounded-contexts.md:139-162`; `docs/vision/domain-events.md:97-106` | No `src/CritterBids.Obligations` project exists. | `bcs/obligations.md` | Declared-but-not-built (whole BC) |

### B2.02 — Relay BC

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| BC scope: SignalR push, notification routing | `docs/vision/bounded-contexts.md:165-186` | No `src/CritterBids.Relay` project exists. | `bcs/relay.md` | Declared-but-not-built (whole BC) |

### B2.03 — Operations BC

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| BC scope: staff dashboard, cross-BC read models, demo reset | `docs/vision/bounded-contexts.md:189-211` | No `src/CritterBids.Operations` project exists. | `bcs/operations.md` | Declared-but-not-built (whole BC) |

### B2.04 — `ParticipantSessionEnded` event

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Participants BC emits `ParticipantSessionEnded` on session timeout or explicit termination | `docs/vision/bounded-contexts.md:29`; `docs/vision/domain-events.md:17` | No CLR type in `src/CritterBids.Contracts/Participants/`. No emitter in `src/`. No session-end command, handler, or expiry path. | `bcs/participants.md`; `glossary.md` `ParticipantSessionEnded` entry | Declared-but-not-built (event) |

### B2.05 — `ListingRevised` event

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Selling emits `ListingRevised` when a seller updates a published listing (Title/Description/ShippingTerms) | `docs/vision/bounded-contexts.md:59`; `docs/vision/domain-events.md:35` | No CLR type in `Contracts/Selling/` or `Selling/`. No emitter. No consumer registration. No `EditPublishedListing` command. | `bcs/selling.md` "Vision-doc capabilities NOT implemented" | Declared-but-not-built (event) |

### B2.06 — `ListingEndedEarly` event

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Selling emits `ListingEndedEarly` for seller-initiated mid-auction termination (distinct from ops-initiated `ListingWithdrawn`) | `docs/vision/bounded-contexts.md:59`; `docs/vision/domain-events.md:36` | No CLR type. No emitter. The semantic of "seller-initiated termination" is folded into `ListingWithdrawn` in code. | `bcs/selling.md`; `glossary.md` `ListingWithdrawn` entry | Declared-but-not-built (event) |

### B2.07 — `ListingRelisted` event

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Selling emits `ListingRelisted` to mark a passed/ended/withdrawn listing being relisted (with `OriginalListingId` / `NewListingId`) | `docs/vision/bounded-contexts.md:59`; `docs/vision/domain-events.md:37` | No CLR type. No emitter. No `RelistListing` command. | `bcs/selling.md` | Declared-but-not-built (event) |

### B2.08 — `LotWatchAdded` / `LotWatchRemoved` events

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Listings BC emits two integration events for watchlist add/remove | `docs/vision/bounded-contexts.md:110`; `docs/vision/domain-events.md:71-72` | No CLR types in `Contracts/Listings/` (no `Listings/` subfolder exists in Contracts). No emitter. No consumer. No watchlist document. | `bcs/listings.md` "Vision-doc capabilities NOT implemented" | Declared-but-not-built (events and capability) |

### B2.09 — Watchlist capability

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Per-participant private watchlist with social-proof watch counts on listings | `docs/vision/bounded-contexts.md:99, 104` | No document, no handler, no endpoint, no counter field on `CatalogListingView`. | `bcs/listings.md` | Declared-but-not-built (capability) |

### B2.10 — Category and price filtering

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Catalog supports per-category and per-price filtering | `docs/vision/bounded-contexts.md:95` | No category field on `CatalogListingView`; no filter endpoints. The two catalog endpoints return all-published / by-id only. | `bcs/listings.md`; `src/CritterBids.Listings/Features/Catalog/CatalogEndpoints.cs` | Declared-but-not-built (capability) |

### B2.11 — Full-text search

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Full-text search via Marten/PostgreSQL on the catalog | `docs/vision/bounded-contexts.md:98` | No search endpoint; no full-text-index configuration in `ListingsModule.cs`. | `bcs/listings.md` | Declared-but-not-built (capability) |

### B2.12 — `SellerProfile` aggregate

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Distinct `SellerProfile` aggregate alongside `Participant` | `docs/vision/bounded-contexts.md:18` | Folded into `Participant.IsRegisteredSeller` boolean. | `bcs/participants.md`; D1.09 above | Declared-but-not-built (aggregate, intentionally folded) |

### B2.13 — All eight Obligations events

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `PostSaleCoordinationStarted`, `ShippingReminderSent`, `DeadlineEscalated`, `TrackingInfoProvided`, `DeliveryConfirmed`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` | `docs/vision/domain-events.md:97-106` | None exist as CLR types anywhere in `src/`. | `bcs/obligations.md` | Declared-but-not-built (events) |

### B2.14 — `DemoResetInitiated` Operations event

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Operations emits `DemoResetInitiated` when staff trigger a demo reset | `docs/vision/domain-events.md:120` | No CLR type. No emitter. No command. The vision doc itself labels it "Post-MVP". | `bcs/operations.md` | Declared-but-not-built (event; vision-doc-acknowledged post-MVP) |

---

## Class 3 — Declared-but-not-wired

Events, commands, or integration messages that exist in code but have no emitter, no consumer, or no registration in `Program.cs`.

### W3.01 — `ProxyBidRegistered` has no `PublishMessage` route

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `ProxyBidRegistered` is published from `StartProxyBidManagerSagaHandler` via `OutgoingMessages` | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:79-85` | No `PublishMessage<ProxyBidRegistered>` route in `Program.cs`. Wolverine routes the message to `tracked.NoRoutes`. Intended consumer is Relay (post-M5). | `src/CritterBids.Api/Program.cs` (no entry for ProxyBidRegistered); `workflows/proxy-bidding.md` Reg-step 5 | Declared-but-not-wired (no publisher route) |

### W3.02 — `ProxyBidExhausted` has no `PublishMessage` route

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `ProxyBidExhausted` is published from `ProxyBidManagerSaga` exhaustion branch via `OutgoingMessages` | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:103-120` | No `PublishMessage<ProxyBidExhausted>` route in `Program.cs`. Lands in `tracked.NoRoutes`. Intended consumer is Relay (post-M5). | `src/CritterBids.Api/Program.cs`; `workflows/proxy-bidding.md` Reactive step R5a | Declared-but-not-wired (no publisher route) |

### W3.03 — `SellerPayoutIssued` published but has no consumer

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `SellerPayoutIssued` routes to `relay-settlement-events` queue | `src/CritterBids.Api/Program.cs:155-156` | No `ListenToRabbitQueue("relay-settlement-events")` wiring; Relay BC project does not exist. | Phase 0 inventory; `bcs/relay.md`; `workflows/post-sale-obligations.md` "Cross-BC publishers without consumers" | Declared-but-not-wired (no listener) |

### W3.04 — `PaymentFailed` published but has no consumer

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `PaymentFailed` routes to `operations-settlement-events` queue | `src/CritterBids.Api/Program.cs:162-163` | No `ListenToRabbitQueue("operations-settlement-events")` wiring; Operations BC project does not exist. | `bcs/operations.md`; `workflows/post-sale-obligations.md` | Declared-but-not-wired (no listener) |

### W3.05 — Other Relay-bound queues have no consumers

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `relay-auctions-events` and similar Relay-destined queue routes exist | `src/CritterBids.Api/Program.cs:107-109` and adjacent | No Relay-side `ListenToRabbitQueue` consumer (project absent). | `bcs/relay.md` | Declared-but-not-wired (no listener) |

### W3.06 — Listings does not consume `SessionCreated`

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Documented possibility in `Contracts.Auctions.SessionCreated.cs:18-25` that Listings may maintain a lightweight `SessionCatalog` view from `SessionCreated` | `src/CritterBids.Contracts/Auctions/SessionCreated.cs:18-25`; `docs/vision/domain-events.md:48` | `AuctionsSessionHandler` in Listings explicitly does NOT handle `SessionCreated` — only `ListingAttachedToSession` and `SessionStarted` (comment: "the catalog has no per-session document"). | `bcs/listings.md` Integration events (in) table; `workflows/flash-session.md` step 3 | Declared-but-not-wired (event has no Listings consumer despite docstring suggesting one) |

### W3.07 — `Listing.cs` aggregate has only one `Apply` method

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| Vision-doc framing implies the `Listing` aggregate evolves through bid/close/extend events | `docs/vision/bounded-contexts.md` Auctions section | `Listing` aggregate only has `Apply(BiddingOpened)`. The bid, reserve, BIN, extension, and close events do not append to the `Listing` stream — they live on `BidConsistencyState` (DCB) or are emitted via `OutgoingMessages` from saga. The terminal outcome events `BiddingClosed`, `ListingSold`, `ListingPassed` are **intentionally not registered** as Marten event types (`AuctionsModule.cs:87-90`). | `src/CritterBids.Auctions/Listing.cs`; `bcs/auctions.md`; `workflows/timed-listing-close.md` Notes | Declared-but-not-wired (vision-doc framing suggests a richer aggregate than code provides; deliberate per DCB design) |

### W3.08 — `Approved` listing status is never observed at rest

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `ListingStatus.Approved` exists as an enum value | `src/CritterBids.Selling/ListingStatus.cs:13` | The submit handler emits `ListingSubmitted + ListingApproved + ListingPublished` atomically in a single transaction (`SubmitListingHandler.cs:54-56`). No consumer ever observes the listing at `Approved` rest. Documented as deliberate (post-MVP migration path to manual approval). | `bcs/selling.md`; `glossary.md` Approved entry | Declared-but-not-wired (state exists as enum value but is transient by design) |

### W3.09 — Several Auctions outcome events lack Marten event registration

| Claim | Source | Reality | Source | Classification |
|---|---|---|---|---|
| `BiddingClosed`, `ListingSold`, `ListingPassed` are domain events on the Auctions side | `docs/vision/domain-events.md:60-62`; `Contracts/Auctions/*.cs` | These cascade through `OutgoingMessages` from `AuctionClosingSaga` only and are never appended to a Marten stream on the Auctions side. They are intentionally NOT in the `UseMandatoryStreamTypeDeclaration` registration in `AuctionsModule.cs:87-90`. | `bcs/auctions.md` Integration events (out); `workflows/timed-listing-close.md` Notes | Declared-but-not-wired (Marten event-type registration deliberately omitted; messages flow via bus only) |

---

## Cross-references

- Glossary entries that flag drift inline: `BidderId`, `FeePercentage`, `ParticipantSessionEnded`, `SellerProfile`, `Lot`.
- Open questions surfaced from drift: `OPEN-QUESTIONS.md` OQ-P2-01 (the `IsProxy` plumbing question) is the formal capture of D1.13.
- Process traces touching drift:
  - `workflows/post-sale-obligations.md` — hops 7-9 cross from implemented into Planned-only code.
  - `workflows/buy-it-now.md` — step 8 notes the ProxyBidDispatchHandler does NOT subscribe to `BuyItNowPurchased`; any active proxy sagas for a BIN'd listing remain `Active` forever. Recorded above as part of the broader Class 3 / W3.07 pattern.

## Out of scope here

- Whether any drift item is good, bad, or worth fixing — this register is descriptive only.
- Suggesting reconciliations or rebuilds — every such judgment lives in `lessons.md` (Phase 5).
