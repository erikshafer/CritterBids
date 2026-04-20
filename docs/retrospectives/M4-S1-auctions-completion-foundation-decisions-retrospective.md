# M4-S1: Auctions Completion Foundation Decisions + Contract Stubs — Retrospective

**Date:** 2026-04-20
**Milestone:** M4 — Auctions BC Completion
**Session:** S1 of 7 (plus pre-drafted S4b and S5b split slots)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/M4-S1-auctions-completion-foundation-decisions.md`
**Baseline:** 86 tests passing · `dotnet build` 0 errors, 0 warnings · M3 complete

---

## Baseline

- 86 tests passing (1 Api + 1 Contracts + 6 Participants + 11 Listings + 32 Selling + 35 Auctions)
- `dotnet build` — 0 errors, 0 warnings
- ADR 007 status: Stream IDs ✅ Accepted; Event Row IDs 🟡 Deferred (Gate 4 open since M3-S1 — second re-evaluation now due)
- `src/CritterBids.Contracts/Auctions/` contains the nine M3-S1 stubs; no proxy or session vocabulary yet
- `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` is the minimal M3-S5b single-field stub `(Guid ListingId)`
- No `AuctionsIdentityNamespaces.cs` exists; proxy-saga composite-key format is Workshop 002 §4.1 text only
- `src/CritterBids.AppHost/Program.cs` uses `AddRabbitMQ("rabbitmq").WithImageTag("3-management")` with no management-UI endpoint published

---

## Items completed

| Item | Description |
|------|-------------|
| S1a | ADR 007 amended with second dated sub-entry in the "Event Row ID Decision — Deferred" section; new full "Event Row ID Decision — Re-Deferred (M4-S1, 2026-04-20)" section added with rationale, new trigger (M5-S1, Settlement BC), named owner (Erik); gate-status table re-dated |
| S1b | `docs/decisions/README.md` — ADR 007 row Summary + Date refreshed; ADR 014 reserved-for-M4-S6 row added; ADR 015 judged non-triggering (M4-D4 resolved to option 4); status key legend extended with 🔒 Reserved |
| S1c | `docs/milestones/M4-auctions-bc-completion.md` — §8 rows for ADR 007 Gate 4, M4-D1, M4-D2, M4-D4 moved from "Resolve in S1" to "Resolved in S1" with pinned choices and rationale; §6 `AttachListingToSession` bullet names option 4 (Auctions-side duplicate `PublishedListings` projection) as the resolution |
| S1d | `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` — new file; `internal static class` with a single `ProxyBidManagerSaga` namespace `Guid` constant (hard-coded `abffa589-fb32-4b62-8ff7-ee1ca4f255ff`); triple-slash docstring cites M4-D1 and Workshop 002 §4.1 |
| S1e | Six new `sealed record` contract stubs in `src/CritterBids.Contracts/Auctions/`: `RegisterProxyBid.cs`, `ProxyBidRegistered.cs`, `ProxyBidExhausted.cs`, `SessionCreated.cs`, `ListingAttachedToSession.cs`, `SessionStarted.cs`; each carries the full future-consumer payload per integration-messaging L2 |
| S1f | `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` extended under ADR 005 additive versioning from `(Guid ListingId)` to `(Guid ListingId, Guid WithdrawnBy, string? Reason, DateTimeOffset WithdrawnAt)`; docstring rewritten to name M4-S2 as producer, document two-queue transport (`auctions-selling-events`, `listings-selling-events`), and record the M3/M4 consumer list |
| S1g | `src/CritterBids.AppHost/Program.cs` — `rabbitMq` builder chained with `.WithManagementPlugin()`; management UI now exposed on the Aspire dashboard |
| S1h | Two M3-era `new ListingWithdrawn(listingId)` call sites updated to supply the three new fields: `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs:198` and `tests/CritterBids.Auctions.Tests/AuctionClosingSagaTests.cs:319` |
| S1i | This retrospective |

The prompt structured scope as five commits rather than lettered items. Commit ↔ item mapping:

| Commit | Items covered |
|--------|---------------|
| 1 — `docs(adr-007)` Gate 4 re-evaluation | S1a, S1b |
| 2 — `docs(m4)` milestone resolutions | S1c |
| 3 — `feat(auctions)` identity namespace | S1d |
| 4 — `feat(contracts)` Auctions stubs + extended `ListingWithdrawn` | S1e, S1f, S1h |
| 5 — `chore(apphost)` RabbitMQ UI + retrospective | S1g, S1i |

---

## S1a — ADR 007 Gate 4: re-deferred, not closed

### Decision

**Gate 4 re-deferred to M5-S1 (Settlement BC foundation-decisions session), owner Erik.** The prompt
offered two acceptable outcomes: close with a JasperFx-informed decision, or re-defer with a named
downstream trigger and a named owner. JasperFx input had not arrived between M3-S1 (2026-04-16) and
M4-S1 (2026-04-20), so closure was not an option; bare re-deferral is explicitly not acceptable per
the prompt.

### Why re-defer rather than accept the engine default

Gate 1 (Marten 8 row-ID seam) remains unconfirmed. Selecting the engine default now would either
(a) re-open at Gate 1 resolution anyway, or (b) lock in a choice that survives only if the engine
default is in fact `Guid.NewGuid()`-shaped. M5-S1 is the last foundation-decisions session across
all Marten BCs, so the re-trigger is natural — at that point all the Marten event streams are in
production-shape code and the cost/benefit of switching generators is quantifiable with concrete
traffic profiles from Participants, Selling, Auctions, and Listings. The risk of drift past M4 is
mitigated by naming Erik as the owner of the JasperFx follow-up rather than leaving the nudge
un-owned (the M3-S1 deferral phrasing).

### Shape of the ADR change

Two edits in `docs/decisions/007-uuid-strategy.md`:
1. Existing "Event Row ID Decision — Deferred (M3-S1)" blockquote in the Acceptance Gates section
   extended with a second dated sub-entry (`Re-Deferred (M4-S1, 2026-04-20)`).
2. A new full section "Event Row ID Decision — Re-Deferred (M4-S1, 2026-04-20)" appended after the
   existing deferred section, mirroring the shape of "Stream ID Decision — Accepted" — status line,
   decision paragraph, rationale, named trigger and owner, updated gate-status table.

This follows the prompt's "Gate 4 amendment shape" convention: extend the existing section with a
dated sub-entry *and* add a fresh dated section, rather than replacing the M3-S1 history.

---

## S1c — M4 milestone resolutions

### M4-D1 — proxy saga composite key format

Resolved. The Workshop 002 §4.1 string form `$"{ListingId}:{BidderId}"` is confirmed as the
deterministic name input to `UuidV5.Create(namespace, name)`. Pinned in code via
`AuctionsIdentityNamespaces.cs` (S1d).

**Why not byte concatenation:** Workshop 002 §4.1 explicitly specifies the colon-delimited string
form. Byte concatenation would be slightly faster but would require a separate documentation
carveout, and UUID v5 hashing cost is not on the hot path for proxy bid traffic.

### M4-D2 — Session aggregate ID strategy

Resolved. UUID v7 (`Guid.CreateVersion7()`) per ADR 007's stream ID section — no natural business
key exists (session titles are not unique; two Flash sessions can share a title) and v7 provides
insert locality for the session stream via its Unix-ms prefix.

**Why not UUID v5 with a natural key:** the Participants BC uses UUID v5 with `email` as the name
input because email is the natural identifier for a participant. Sessions have no equivalent unique
natural identifier in the ops-staff workflow — choosing v5 would require inventing an artificial
name (e.g. `$"{Title}:{CreatedAt}"`) that adds no value over an opaque v7 Guid.

### M4-D4 — `AttachListingToSession` published-status check: option 4 (Auctions-side duplicate projection)

Resolved. The four candidates were evaluated explicitly:

| Option | Shape | Rejection reason |
|---|---|---|
| 1 | Cross-BC query of `CatalogListingView` from Auctions | Violates modular-monolith BC isolation rule; would trigger ADR 015 "Cross-BC Read Access from Handlers" |
| 2 | Lightweight Selling-side "listing-available" signal event | Adds a new integration event purely to avoid a duplicate projection; `ListingPublished` / `ListingWithdrawn` already carry the information |
| 3 | Accept handler-time ambiguity (no guard) | Violates Workshop 002 §5.3 — the `AttachListingToSession` command must reject if the listing is not `Published` |
| **4** | **Auctions-side duplicate `PublishedListings` projection subscribing to `ListingPublished` / `ListingWithdrawn`** | **Chosen** — preserves BC isolation; duplicate projections across BCs are a named modular-monolith pattern; no ADR triggered |

**Consequence:** S5 picks up the residual — extra event-subscription wiring (`ListingPublished` and
`ListingWithdrawn` consumers inside Auctions BC), a Marten projection authoring step
(`PublishedListings` view), catch-up on startup for replayability, and tests. §9 session sizing
already flagged this trigger for S5b. The M4 plan §6 bullet and §8 row were both updated to name
option 4 and to record that no ADR 015 candidate is flagged at M4-S1.

---

## S1d — `AuctionsIdentityNamespaces.cs`: pure constant file

### Shape at session close

```csharp
namespace CritterBids.Auctions;

internal static class AuctionsIdentityNamespaces
{
    public static readonly Guid ProxyBidManagerSaga =
        new Guid("abffa589-fb32-4b62-8ff7-ee1ca4f255ff");
}
```

### Why this is the full file

Per the prompt: no saga logic, no `UuidV5.Create(...)` helper, no other members. The saga author at
S3 consumes this namespace via the helper they author there — S1's only job is to pin the Guid so
the saga's composite-key format is stable across sessions. Accessibility is `internal` because both
the saga and the namespace live in `CritterBids.Auctions`; nothing cross-project touches this.

### Precedent

Matches the M2 `CritterBids.Participants.ParticipantsConstants.ParticipantsNamespace` shape and the
internal `BidRejectionAudit.Namespace` constant in `CritterBids.Auctions/BidRejected.cs`. Hard-coded
Guid literal rather than a `Guid.NewGuid()`-at-runtime because the value must be deterministic
across deployments.

---

## S1e/S1f — Contract stubs (six new + one extended)

### Files authored (all `sealed record`, namespace `CritterBids.Contracts.Auctions` unless noted)

| File | Payload | Primary consumer |
|---|---|---|
| `Auctions/RegisterProxyBid.cs` | `ListingId, BidderId, MaxAmount` | Auctions BC (M4-S3) — `ProxyBidManagerSaga` start |
| `Auctions/ProxyBidRegistered.cs` | `ListingId, BidderId, MaxAmount, RegisteredAt` | Relay BC (post-M5) — audit notification |
| `Auctions/ProxyBidExhausted.cs` | `ListingId, BidderId, MaxAmount, ExhaustedAt` | Relay BC (post-M5) — distinct "your proxy has been exceeded" notification per W001 Parked #3 |
| `Auctions/SessionCreated.cs` | `SessionId, Title, DurationMinutes, CreatedAt` | Listings BC (M4-S6), Operations (post-M5), Relay (not a direct consumer) |
| `Auctions/ListingAttachedToSession.cs` | `SessionId, ListingId, AttachedAt` | Listings BC (M4-S6) — `SessionMembershipHandler` |
| `Auctions/SessionStarted.cs` | `SessionId, IReadOnlyList<Guid> ListingIds, StartedAt` | Auctions BC internally (M4-S5 fan-out via `SessionStartedHandler`), Listings BC (M4-S6) |
| `Selling/ListingWithdrawn.cs` (extended) | `ListingId, WithdrawnBy, Reason?, WithdrawnAt` | Auctions M3 (Closing saga), Auctions M4-S4 (Proxy saga terminal), Listings M4-S6, Relay post-M5, Operations post-M5 |

### Integration-messaging L2 discipline

Each record carries every field any currently-named future consumer needs, not just the M4
consumer. `ProxyBidExhausted.MaxAmount` is on the event (not only saga state) so Relay can render
the post-M5 notification text without a follow-up lookup. `SessionCreated.DurationMinutes` is on
the event even though M4-S6's initial `CatalogListingView` membership fields only read `SessionId`
and `Title` — Operations's post-M5 live-board countdown needs it and Listings's future
`SessionCatalog` will need it.

### ADR 005 additive versioning for `ListingWithdrawn`

The extension appends three new positional fields to the existing single-field shape. Key property:
any deserializer compiled against the M3-era `(Guid ListingId)` record continues to round-trip the
extended payload without a code change. `ListingId` is preserved verbatim — renaming or re-typing
it would be a breaking change and is explicitly not done here.

**Caveat — constructor-level vs wire-level.** The ADR 005 guarantee is runtime/wire; it does not
cover *compile-time* call sites using positional-constructor syntax. Two M3-era synthetic-producer
call sites in the Auctions test project broke on the first build (`CS7036` — `WithdrawnBy`
required). Both were mechanical three-field additions using realistic seller-withdrawal defaults
(`WithdrawnBy: Guid.NewGuid()`, `Reason: null`, `WithdrawnAt: DateTimeOffset.UtcNow`), since the
M3 Auction Closing saga scenario 3.10 does not read the new fields. This is item S1h.

---

## S1g — Aspire RabbitMQ management UI

### Change

```csharp
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("3-management")
    .WithManagementPlugin()
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");
```

### Why not the `.WithEndpoint(targetPort: 15672, ...)` fallback

`Aspire.Hosting.RabbitMQ 13.2.2` (centrally managed in `Directory.Packages.props`) exposes the
`.WithManagementPlugin()` extension directly — the first-try compile succeeded. The prompt's
fallback (`.WithEndpoint(targetPort: 15672, ...)`) was not needed. `WithImageTag("3-management")`
was already in place from M2.5 so the underlying container image already had the management
plugin; only the Aspire endpoint wiring was missing.

---

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Baseline (M3 close) | 86 | ✅ |
| After contract stub authoring (pre-`ListingWithdrawn` extension) | 86 | N/A (build only) |
| After `ListingWithdrawn` extension | — | ❌ build failed — 2× `CS7036` in Auctions.Tests (two M3-era call sites on the single-field constructor) |
| After test call-site fixup | 86 | ✅ |
| After `.WithManagementPlugin()` | 86 | ✅ |

Final: **86 passing** (1 Api + 1 Contracts + 6 Participants + 11 Listings + 32 Selling + 35 Auctions).
Matches M3 close baseline exactly — no new tests, no deleted tests, no regression.

---

## Build state at session close

- `dotnet build`: 0 errors, 0 warnings
- `dotnet test`: 86 passing, 0 failing, 0 skipped
- Integration events in `src/CritterBids.Contracts/Auctions/`: 15 (9 from M3-S1 + 6 new M4-S1)
- Contract records with "Event" suffix in type name: 0 (convention held)
- `List<T>` usage on any record: 0 — `SessionStarted.ListingIds` is `IReadOnlyList<Guid>` per convention
- Files touching `CritterBids.Api/Program.cs`: 0 (no publisher routing this session per explicit non-goal)
- Files touching `CritterBids.Auctions/*.cs` other than the new `AuctionsIdentityNamespaces.cs`: 0
- New ADR files: 0 (ADR 014 reserved row only; ADR 015 not triggered)

---

## Key learnings

1. **ADR 005 additive versioning is wire-safe but not source-safe.** The extension of
   `ListingWithdrawn` was a one-line record-signature change, yet it broke two test call sites at
   compile time because C# positional records have no default parameter values. Future sessions
   extending contract records should grep for all constructor call sites before the extension ships
   — `dotnet build` catches them reliably, but the pre-flight check shortens the loop.

2. **Re-deferrals need owners, not just triggers.** The M3-S1 Gate 4 deferral was trigger-only
   ("re-evaluate at M4-S1 if JasperFx input lands"). The natural drift failure mode is that the
   trigger fires and nothing happens because nobody is named for the follow-up. M4-S1's re-deferral
   adds Erik as the owner of the JasperFx nudge — the template for any future deferral.

3. **Duplicate projections across BCs are a cheaper isolation-preserver than cross-BC reads.** M4-D4
   had four credible options; the one that preserved BC isolation without requiring a new ADR was
   option 4 (duplicate projection subscribing to the same Selling events Listings already subscribes
   to). The pattern is the modular-monolith equivalent of "each microservice materializes its own
   view" — the cost is one extra projection per consuming BC, not a new integration contract.

4. **Integration-messaging L2 is worth the per-event payload overhead.** The six new stubs all
   carry fields that no M4 consumer reads — `ProxyBidExhausted.MaxAmount` is for Relay post-M5,
   `SessionCreated.DurationMinutes` is for Operations post-M5. Shipping the full payload at first
   commit means M5 integration sessions add handlers, not contract versions.

5. **Vocabulary-lock sessions benefit from a pure-constants artifact.** `AuctionsIdentityNamespaces.cs`
   is 10 lines of code but pins M4-D1 in a form that S3's saga author cannot accidentally drift from.
   The alternative — leaving the composite-key format as prose in the M4 milestone doc — invites
   S3 to re-derive it and get it wrong. Pattern candidate for future BC foundation sessions.

---

## Verification checklist

- [x] `docs/decisions/007-uuid-strategy.md` — updated with the second Gate 4 re-evaluation outcome; status header reflects the updated state
- [x] `docs/decisions/README.md` — ADR 007 row Summary and Date updated; ADR 014 placeholder row present with status "🔒 Reserved for M4-S6 authorship"; no ADR 015 row (M4-D4 resolved to option 4, no trigger)
- [x] `docs/milestones/M4-auctions-bc-completion.md` — §8 rows for M4-D1, M4-D2, M4-D4 moved to "Resolved in S1" with pinned choices; §6 `AttachListingToSession` bullet names option 4
- [x] `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` — new file; single `ProxyBidManagerSaga` namespace Guid constant; triple-slash docstring cites M4-D1 + Workshop 002 §4.1; no helper methods
- [x] `src/CritterBids.Contracts/Auctions/` — six new `.cs` files (`RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`); each is a `sealed record` with final field list and triple-slash docstring
- [x] `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` — extended to `(Guid ListingId, Guid WithdrawnBy, string? Reason, DateTimeOffset WithdrawnAt)`; docstring updated with M4-S2 producer pointer and ADR 005 rationale
- [x] `src/CritterBids.AppHost/Program.cs` — `rabbitMq` builder chained with `.WithManagementPlugin()`; no other AppHost changes
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] `dotnet test` — 86 passing (M3 close baseline unchanged)
- [x] This retrospective

---

## Scope deviations

**Test call-site fixup (S1h) was not in the prompt's explicit item list** but is a necessary
consequence of the `ListingWithdrawn` extension. Two M3-era call sites used the positional
single-field constructor; extending the record without updating them would have left the build
broken. The fixup stayed within the Auctions test project — no production code was touched, and
the scenarios under test (3.10, withdrawal terminal path) continue to read only `ListingId`.

No ADR 015 candidate was flagged — M4-D4 resolved to option 4, which does not trigger cross-BC
read-access authorization, so the conditional ADR 015 placeholder row was not added.

---

## What M4-S2 should know

M4-S2 is the Selling-side `WithdrawListing` command handler authoring session. Three S1 outputs
matter directly:

1. **`ListingWithdrawn` now carries four fields, not one.** The M3-S5b fixture synthesis of the
   single-field form is gone from production code paths; M4-S2's command handler becomes the
   canonical producer and must populate `WithdrawnBy` from the command, `Reason` as nullable
   (MVP: always null from the seller-withdrawal command), and `WithdrawnAt` as a handler-stamped
   `DateTimeOffset.UtcNow` (not the outbox-dispatch time).
2. **Two publisher-side routing rules are needed** in `CritterBids.Api/Program.cs`:
   `auctions-selling-events` (existing queue, already consumed by Auctions) and
   `listings-selling-events` (existing queue, already consumed by Listings). Both queues exist;
   only the publisher-side fan-out routing is new.
3. **The Auctions-side test call-site pattern is already in place** — the `WithdrawnBy`-then-null
   shape used in `AuctionsTestFixture.AppendListingWithdrawnAsync` is the reference for any S2
   test that constructs `ListingWithdrawn` directly. M4-S4's proxy-saga terminal path consumes
   the same event without caring about the new fields.

Gate 4 remains open — the next formal re-evaluation is M5-S1. If JasperFx input arrives before
then, close Gate 4 immediately in an interim session rather than waiting for M5-S1. Erik owns the
nudge.
