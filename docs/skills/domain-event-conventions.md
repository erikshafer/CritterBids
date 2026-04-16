# Domain Event Conventions

Conventions for naming, placing, and structuring domain events in CritterBids. Load this skill
whenever you are adding an event to a BC's event stream. These conventions were established in
M2-S5 and M2-S6 through the Selling BC's event model.

---

## 1. Naming

Past-tense verb + noun:

```csharp
DraftListingCreated    // ✅ verb=Created, noun=DraftListing
ListingSubmitted       // ✅ verb=Submitted, noun=Listing
ListingPublished       // ✅ verb=Published, noun=Listing
```

Rules:
- **No `Event` suffix** — `ListingPublishedEvent` ❌. The namespace and usage context make the type role obvious.
- Describe the fact that happened, not the command that caused it (`ListingSubmitted`, not `SubmitListingRequested`)
- Noun is the aggregate or primary entity; verb is the state transition or fact recorded

---

## 2. File and Namespace Placement

One event per file, named identically to the type:

```
src/CritterBids.Selling/
  DraftListingCreated.cs     // ✅
  ListingSubmitted.cs        // ✅
  ListingPublished.cs        // ✅ — the DOMAIN event, internal to Selling BC
```

Namespace: `CritterBids.{BcName}` — the BC that owns the event stream:

```csharp
namespace CritterBids.Selling;

public sealed record DraftListingCreated(...);
```

**Domain events are never placed in `CritterBids.Contracts`.** Contracts carries integration events for
cross-BC messaging. A domain event with the same name as an integration contract (e.g., `ListingPublished`)
lives in the BC's own namespace — see §5 and §7.

---

## 3. Type Shape

```csharp
// ✅ Canonical shape — from CritterBids.Selling
public sealed record DraftListingCreated(
    Guid ListingId,                          // aggregate ID first — see §4
    Guid SellerId,
    string Title,
    ListingFormat Format,                    // BC-owned enum — see §8
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow,
    TimeSpan? ExtendedBiddingExtension,
    DateTimeOffset CreatedAt);               // DateTimeOffset — never DateTime
```

Rules:
- `sealed record` — no exceptions
- Properties are positional `init`-only — consistent with the rest of the BC
- `DateTimeOffset` for all timestamps — never `DateTime`
- `IReadOnlyList<T>` for collections — never `List<T>`
- No navigation properties, no methods, no behavior

---

## 4. Aggregate ID Field Naming

Use `{AggregateTypeName}Id` — unambiguous when events are read in isolation (projections, handlers):

```csharp
// Aggregate type: SellerListing → ID field: ListingId
public sealed record ListingSubmitted(
    Guid ListingId,       // ✅
    Guid SellerId,
    DateTimeOffset SubmittedAt);

// NOT:
public sealed record ListingSubmitted(
    Guid Id,              // ❌ — ambiguous when read in projections or handlers
    ...);
```

---

## 5. Slim Domain Events vs Rich Integration Contracts

**This is the most important convention in this document.**

Domain events carry only the data needed to reconstruct aggregate state. Integration contracts
(in `CritterBids.Contracts`) carry the full payload for all downstream consumers.

From the Selling BC's `SubmitListing` handler (M2-S6):

```csharp
// CritterBids.Selling.ListingPublished — domain event, 2 fields
public sealed record ListingPublished(
    Guid ListingId,
    DateTimeOffset PublishedAt);

// CritterBids.Contracts.Selling.ListingPublished — integration contract, 13 fields
public sealed record ListingPublished(
    Guid ListingId, Guid SellerId, string Title, string Format,
    decimal StartingBid, decimal? ReservePrice, decimal? BuyItNow,
    TimeSpan? Duration, bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow, TimeSpan? ExtendedBiddingExtension,
    decimal FeePercentage, DateTimeOffset PublishedAt);
```

Rationale:
- Keeps the event stream compact — only state-reconstruction data lives in the stream
- Prevents downstream BCs from coupling to aggregate internals
- Allows the integration contract to evolve independently from the domain event

**Implication:** The aggregate must have sufficient fields to construct the integration contract
at publish time. In M2-S6, `SellerListing` needed `Format`, `Duration`, and `ExtendedBidding*`
fields added before `SubmitListingHandler` could construct the `Contracts.Selling.ListingPublished`
payload. Plan aggregate fields and integration contract fields together.

---

## 6. Marten Event Type Registration

Every domain event that appears in a Marten event stream must be registered in the BC's
`ConfigureMarten()` call:

```csharp
// In SellingModule.cs — AddSellingModule()
services.ConfigureMarten(opts =>
{
    opts.Events.AddEventType<DraftListingCreated>();
    opts.Events.AddEventType<DraftListingUpdated>();
    opts.Events.AddEventType<ListingSubmitted>();
    opts.Events.AddEventType<ListingApproved>();
    opts.Events.AddEventType<ListingRejected>();
    opts.Events.AddEventType<ListingPublished>();
});
```

`UseMandatoryStreamTypeDeclaration` is set globally in `Program.cs`. Omitting a registration
causes **silent `null` returns** from `AggregateStreamAsync<T>` for streams that include the
unregistered event type — no compile-time error, no startup error. Register every event type
in the same commit that introduces it.

---

## 7. Naming Collisions Between Domain and Integration Events

When a domain event and an integration contract share the same simple name (e.g., both called
`ListingPublished` in different namespaces), alias the integration contract in the handler file:

```csharp
// In SubmitListing.cs
using ContractListingPublished = CritterBids.Contracts.Selling.ListingPublished;

// Handler references both without ambiguity:
events.Add(new ListingPublished(listing.Id, publishedAt));              // domain event
outgoing.Add(new ContractListingPublished(listing.Id, ...));            // integration contract
```

In `Program.cs` or other host-level files, use fully qualified names rather than `using` aliases
to keep the source namespace visible:

```csharp
opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
    .ToRabbitQueue("listings-selling-events");
```

---

## 8. Enum Types That Appear in Events

Enum types used in domain events are defined in the BC's own namespace — never in
`CritterBids.Contracts`:

```csharp
// src/CritterBids.Selling/ListingFormat.cs — ✅ BC-owned enum
namespace CritterBids.Selling;
public enum ListingFormat { Flash, Timed }
```

If a downstream BC or integration contract consumer needs to interpret a format or type field,
the integration contract carries a `string` representation — not the enum:

```csharp
// In Contracts.Selling.ListingPublished:
string Format,   // ✅ "Flash" or "Timed" — consumers define their own enum if needed
```

Cross-BC enum sharing is not permitted. Moving an enum to `CritterBids.Contracts` would couple
all consuming BCs to the producing BC's internal type vocabulary.

---

## Related Skills

- `marten-event-sourcing.md` — aggregate `Apply()` methods, event stream registration, `UseMandatoryStreamTypeDeclaration`
- `integration-messaging.md` — integration contracts, consumer table design, RabbitMQ routing
- `adding-bc-module.md` — `AddEventType<T>()` placement in `ConfigureMarten()`, BC module registration
