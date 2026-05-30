---
name: domain-event-conventions
description: "CritterBids domain event conventions: no Event suffix, past-tense names, aggregate ID first, slim stream payloads, contract collisions, and event registration. Use when adding events."
cluster: design
tags: [domain-events, naming, marten, contracts, event-sourcing]
---

# Domain Event Conventions

> CritterBids conventions for naming, placing, and shaping domain events.
> Generic Marten aggregate mechanics live in ai-skills `marten-aggregate-handler-workflow`; **this skill documents only CritterBids-specific event vocabulary and payload decisions.**

## When to apply this skill

Use this skill when:

- Adding a domain event to a BC event stream.
- Naming an integration message that mirrors a domain event.
- Deciding what belongs in a domain event vs `CritterBids.Contracts`.
- Registering event types in `ConfigureMarten()`.

Do NOT use this skill for: handler mutation mechanics (see `marten-event-sourcing`) or cross-BC routing (see `integration-messaging`).

## Read upstream first

Generic mechanics are covered upstream. Read this ai-skill (license required; install via `npx skills add`) before this skill when implementing handlers:

1. `marten-aggregate-handler-workflow` — aggregate handler workflow, returned events, stream writes.

That covers framework mechanics. This skill picks up at CritterBids naming and payload conventions.

## Naming rules

- No `Event` suffix: `ListingPublished`, not `ListingPublishedEvent`.
- Name the fact that happened, not the command: `ListingSubmitted`, not `SubmitListingRequested`.
- Use noun + past-tense verb when natural in CritterBids vocabulary: `BidPlaced`, `BiddingOpened`, `ListingSold`, `ListingPassed`.
- Use canonical outcome names: `ListingSold` for happy path; `ListingPassed` for no bids or reserve not met.

```csharp
public sealed record DraftListingCreated(...);
public sealed record ListingSubmitted(...);
public sealed record ListingPublished(...);
```

## File and namespace placement

One event per file, named like the type, in the owning BC namespace:

```text
src/CritterBids.Selling/
  DraftListingCreated.cs
  ListingSubmitted.cs
  ListingPublished.cs
```

```csharp
namespace CritterBids.Selling;

public sealed record DraftListingCreated(...);
```

Domain events never live in `CritterBids.Contracts`. Contracts are integration messages for cross-BC consumers.

## Type shape

```csharp
public sealed record DraftListingCreated(
    Guid ListingId,
    Guid SellerId,
    string Title,
    ListingFormat Format,
    decimal StartingBid,
    decimal? ReservePrice,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    DateTimeOffset CreatedAt);
```

Rules:

- `sealed record`.
- Aggregate ID first, named `{AggregateTypeName}Id` or established domain ID (`ListingId`).
- `DateTimeOffset` for timestamps.
- `IReadOnlyList<T>` for collections.
- No navigation properties, methods, or behavior.
- Event payloads must serve all known in-process projections/aggregate reconstruction needs.

## Domain events vs integration contracts

Domain events are slim: only data needed to reconstruct the aggregate and feed in-BC projections.
Integration contracts are rich: data required by all downstream BC consumers.

```csharp
// Domain event in CritterBids.Selling
public sealed record ListingPublished(Guid ListingId, DateTimeOffset PublishedAt);

// Integration contract in CritterBids.Contracts.Selling
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    string Format,
    decimal StartingBid,
    decimal? ReservePrice,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    DateTimeOffset PublishedAt);
```

Design the aggregate state and integration contract together. If a downstream consumer needs a field, the aggregate must have enough state at publish time to build the contract without reading another BC.

## Marten event registration

`UseMandatoryStreamTypeDeclaration` is set globally. Register every domain event in the BC module in the same change that introduces it:

```csharp
services.ConfigureMarten(opts =>
{
    opts.Events.AddEventType<DraftListingCreated>();
    opts.Events.AddEventType<ListingSubmitted>();
    opts.Events.AddEventType<ListingPublished>();
});
```

Omitting registration can produce silent `null` aggregate reads for streams containing unregistered event types.

## Name collisions

Domain event and integration contract may share a simple name. Keep namespaces distinct and alias in handler files:

```csharp
using ContractListingPublished = CritterBids.Contracts.Selling.ListingPublished;

events.Add(new ListingPublished(listing.Id, publishedAt));
outgoing.Add(new ContractListingPublished(listing.Id, sellerId, title, ...));
```

In host-level route config, prefer fully-qualified contract names so the source namespace stays visible.

## Enum fields

Enums used in domain events are BC-owned. Do not move enums to `CritterBids.Contracts` for cross-BC reuse. Integration contracts carry strings when consumers need a portable value.

## Common pitfalls

- **Adding `Event` suffix.** Never do this in CritterBids domain or integration event names.
- **Putting domain events in Contracts.** Contracts are integration API, not event stream internals.
- **Using `Id` as first field.** Use `ListingId`, `SellerId`, etc. so projections are readable in isolation.
- **Making stream events too rich.** Put downstream payload breadth in integration contracts.
- **Forgetting `AddEventType<T>()`.** Mandatory event type declaration makes registration load-bearing.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-aggregate-handler-workflow` — returned events and aggregate write flow.

**Prerequisites:**

- `csharp-coding-standards` — sealed records, collections, time, vocabulary.

**Downstream:**

- `marten-event-sourcing` — stream identity and aggregate state.
- `integration-messaging` — integration contracts and route design.
- `adding-bc-module` — where `AddEventType<T>()` lives.

**External:**

- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions and Key Domain Vocabulary.
- [`docs/vision/domain-events.md`](../../vision/domain-events.md) — canonical event vocabulary.
