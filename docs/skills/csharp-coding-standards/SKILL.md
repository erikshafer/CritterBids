---
name: csharp-coding-standards
description: "CritterBids C# standards: C# 14, sealed records, IReadOnlyList, value objects, naming, Guid v7, DateTimeOffset, financial rounding, and banned vocabulary. Use when authoring domain/application code."
cluster: core
tags: [csharp, coding-standards, domain, records, naming]
---

# C# Coding Standards

> CritterBids C# language, style, and domain vocabulary rules.
> No direct ai-skills equivalent was found for these project coding standards; **this skill documents CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- Writing or reviewing C# in any CritterBids project.
- Creating commands, queries, domain events, integration messages, DTOs, value objects, aggregates, or read models.
- Choosing collection, ID, time, money, or naming patterns.
- Checking banned vocabulary and project conventions.

Do NOT use this skill for: generic Wolverine handler mechanics (see `wolverine-message-handlers`) or Marten aggregate workflow (see `marten-event-sourcing`).

## Read upstream first

No ai-skills file directly owns CritterBids C# style rules. If the code is a handler or aggregate, read the relevant upstream mechanics first:

1. `wolverine-handlers-fundamentals` — generic handler signatures and discovery.
2. `marten-aggregate-handler-workflow` — generic aggregate handler workflow.

Those cover framework mechanics. This skill picks up at CritterBids naming, type shape, and vocabulary.

## Core principles

- C# 14 / .NET 10 language features are allowed.
- Immutability by default: records, init-only properties, readonly collection interfaces, `with` expressions.
- Sealed by default: commands, events, queries, read models, DTOs, and value objects are `sealed record`.
- Pure decision functions where possible; handlers return events/messages and let Wolverine/Marten persist.
- Domain vocabulary beats framework vocabulary: use CritterBids terms consistently.

## Records and collections

```csharp
public sealed record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount);

public sealed record BidPlaced(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    DateTimeOffset PlacedAt);

public sealed record ListingSummaryView(
    Guid Id,
    string Title,
    decimal CurrentHighBid,
    int BidCount,
    string Status);
```

Collections on records/aggregates:

- `IReadOnlyList<T>` for ordered collections.
- `IReadOnlyCollection<T>` for unordered collections.
- `IReadOnlyDictionary<TKey,TValue>` for maps.
- Never expose `List<T>` on messages, records, or aggregates.
- Use collection expression `[]` for empty values.

```csharp
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    IReadOnlyList<string> PhotoUrls,
    IReadOnlyList<string> Tags);
```

## Value objects

Use value objects for constrained domain concepts, not every primitive.

Standard shape:

1. `sealed record` with `Value`.
2. `From(value)` factory with validation.
3. Private parameterless constructor for Marten/JSON.
4. Optional implicit conversion for queries/display.
5. JSON converter when wire format should be primitive.

```csharp
[JsonConverter(typeof(ListingTitleJsonConverter))]
public sealed record ListingTitle
{
    private const int MaxLength = 120;

    public string Value { get; init; } = null!;
    private ListingTitle() { }

    public static ListingTitle From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Listing title cannot be empty", nameof(value));

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"Listing title cannot exceed {MaxLength} characters", nameof(value));

        return new ListingTitle { Value = trimmed };
    }

    public static implicit operator string(ListingTitle title) => title.Value;
    public override string ToString() => Value;
}
```

## Validation, status, and factories

- Nest FluentValidation validators inside command records when used.
- Prefer one status enum over multiple booleans; impossible states should be impossible.
- Use factory methods for creation when invariants or IDs/timestamps are assigned.
- Keep constructors private where Marten/JSON need them.

```csharp
public enum ListingStatus { Pending, Open, Closed, Withdrawn }

public sealed record ParticipantSession
{
    public Guid Id { get; init; }
    public Guid BidderId { get; init; }
    public string DisplayName { get; init; } = null!;
    public DateTimeOffset StartedAt { get; init; }

    private ParticipantSession() { }

    public static ParticipantSession Create(int bidderNumber) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BidderId = Guid.CreateVersion7(),
            DisplayName = $"Bidder {bidderNumber}",
            StartedAt = DateTimeOffset.UtcNow
        };
}
```

## Nullability and async

- Nullable reference types stay enabled.
- Required message fields are non-nullable and not optional-with-default.
- Nullable fields only when business logic truly allows absence or versioning requires it.
- Async all the way; do not call `.Result`/`.Wait()`.
- Return `Task` directly when no `await` is needed.

## IDs, time, and money

- Default new IDs: `Guid.CreateVersion7()` for DB locality.
- UUID v5 only when a natural business key requires deterministic stream ID resolution.
- Use `DateTimeOffset` for all timestamps; use UTC.
- Use `decimal` for money.
- Be explicit about midpoint rounding in fees/settlement math.

```csharp
var fee = Math.Round(hammerPrice * feeRate, 2, MidpointRounding.AwayFromZero);
```

`Math.Round(decimal, int)` uses banker's rounding by default. Test midpoint values like `6.825m` and `6.835m`.

## .NET 10 DCB tag gotcha

`Guid` has public `Variant` and `Version` properties in .NET 10, which breaks Marten DCB tag validation for raw `Guid` tag types. Wrap tag values:

```csharp
public sealed record ListingStreamId(Guid Value);
opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<Listing>();
```

## Naming and vocabulary

| Type | Convention | Example |
|---|---|---|
| Command | Verb + Noun | `PlaceBid`, `PublishListing` |
| Query | Get + Noun | `GetListing`, `GetBidHistory` |
| Domain event | Noun + Past Verb | `BidPlaced`, `ListingSold` |
| Integration message | Noun + Past Verb | `SettlementCompleted` |
| Handler | Command/Query + Handler | `PlaceBidHandler` |
| Validator | Command/Query + Validator | `PlaceBidValidator` |
| Aggregate | Domain noun | `Listing`, `ParticipantSession` |
| Saga | Domain noun + Saga | `AuctionClosingSaga` |

Rules:

- No `Event` suffix: `BidPlaced`, not `BidPlacedEvent`.
- Use `BidderId`, never "paddle".
- Public domain word is `Listing`, not lot.
- Use `Starting Bid`, not opening bid.
- Use `Hammer Price` for final bid before fees.
- Use `Final Value Fee` for platform fee charged to seller.
- Outcomes: `ListingSold` and `ListingPassed`.
- File-scoped namespaces.

## Common pitfalls

- **Dropping `sealed`.** Commands/events/queries/read models must be sealed records.
- **Using `List<T>`.** Prefer read-only interfaces on public shapes.
- **Adding optional defaults to required message fields.** This hides construction bugs.
- **Using `DateTime`.** Use `DateTimeOffset.UtcNow`.
- **Relying on banker's rounding accidentally.** Specify `MidpointRounding` in financial logic.
- **Leaking stale terms.** No "paddle", no public "lot", no "Event" suffix.

## See also

**Upstream (ai-skills)** — generic mechanics this skill may sit beside. License required; install via `npx skills add`:

- No direct equivalent found. For framework-specific code, use `wolverine-handlers-fundamentals` and `marten-aggregate-handler-workflow`.

**Prerequisites:**

- None.

**Downstream:**

- `domain-event-conventions` — detailed event naming/payload rules.
- `wolverine-message-handlers` — handler and endpoint shape.
- `marten-event-sourcing` — stream identity and aggregate persistence.

**External:**

- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions and Key Domain Vocabulary.
