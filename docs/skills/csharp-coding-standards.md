# C# Coding Standards

C# language features, code style, and best practices for CritterBids.

---

## Core Principles

1. **Immutability by default** — records, readonly collections, `with` expressions
2. **Sealed by default** — prevent unintended inheritance
3. **Value objects for domain concepts** — wrap primitives with validation
4. **Pure functions where possible** — separate decisions from side effects

---

## Records and Immutability

Use records for commands, queries, events, DTOs, and value objects:

```csharp
// Commands
public sealed record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount);

// Domain events
public sealed record BidPlaced(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount, DateTimeOffset PlacedAt);

// Value objects
public sealed record Money(decimal Amount, string Currency);

// DTOs / view models
public sealed record ListingSummaryView(Guid Id, string Title, decimal CurrentHighBid, int BidCount, string Status);
```

Use `with` expressions for immutable aggregate updates:

```csharp
public Listing Apply(BidPlaced @event) =>
    this with
    {
        CurrentHighBid = @event.Amount,
        HighBidderId = @event.BidderId,
        BidCount = BidCount + 1
    };
```

---

## Sealed by Default

All commands, queries, events, and models must be `sealed`:

```csharp
// ✅ CORRECT
public sealed record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount);

// ❌ WRONG — allows unintended inheritance
public record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount);
```

---

## Collection Patterns

Always use immutable collection interfaces:

```csharp
// ✅ CORRECT
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    IReadOnlyList<string> PhotoUrls,   // ordered
    IReadOnlyList<string> Tags);       // ordered

// ❌ WRONG — externally mutable
public sealed record ListingPublished(
    Guid ListingId,
    List<string> PhotoUrls,
    List<string> Tags);
```

**Prefer:**
- `IReadOnlyList<T>` for ordered collections
- `IReadOnlyCollection<T>` for unordered collections
- `IReadOnlyDictionary<TKey, TValue>` for key-value pairs

Empty collection shorthand (C# 12+):

```csharp
IReadOnlyList<string> tags = [];
```

---

## Value Object Pattern

Use value objects to wrap primitives with domain-specific validation.

**Standard structure:**
1. `sealed record` with a `Value` property
2. `From(value)` factory method with validation
3. Private parameterless constructor for Marten/JSON deserialization
4. Implicit conversion operator for seamless queries
5. `JsonConverter` for transparent serialization

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

    public static implicit operator string(ListingTitle t) => t.Value;
    public override string ToString() => Value;
}

public sealed class ListingTitleJsonConverter : JsonConverter<ListingTitle>
{
    public override ListingTitle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } value ? ListingTitle.From(value) : throw new JsonException("Title cannot be null");

    public override void Write(Utf8JsonWriter writer, ListingTitle value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
```

Value objects serialize as plain strings — not wrapped objects:

```json
{ "title": "Vintage Rolex Submariner, 1968", "startingBid": 500.00 }
```

**When to use value objects:**
- Identity values with constraints
- Domain concepts with rules (title, display name, tracking number)
- Values requiring validation (money, percentage)

**When NOT to use:**
- Strings with no constraints (descriptions, free text)
- Primitives with no business rules (counts, flags)

---

## FluentValidation

Nest validators inside the command record:

```csharp
public sealed record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount)
{
    public class PlaceBidValidator : AbstractValidator<PlaceBid>
    {
        public PlaceBidValidator()
        {
            RuleFor(x => x.ListingId).NotEmpty();
            RuleFor(x => x.BidId).NotEmpty();
            RuleFor(x => x.BidderId).NotEmpty();
            RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(10, 2, false);
        }
    }
}
```

---

## Status Enums

Use a single status enum over multiple booleans. Impossible states should be impossible.

```csharp
// ✅ CORRECT — single source of truth
public enum ListingStatus { Pending, Open, Closed, Withdrawn }

public sealed record Listing(Guid Id, ListingStatus Status, /* ... */)
{
    public bool IsTerminal => Status is ListingStatus.Closed or ListingStatus.Withdrawn;
    public bool AcceptsBids => Status == ListingStatus.Open;
}

// ❌ WRONG — multiple booleans create ambiguous combinations
public sealed record Listing(Guid Id, bool IsOpen, bool IsClosed, bool IsWithdrawn);
```

---

## Factory Methods

Use static factory methods for object creation. Keep constructors private.

```csharp
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

---

## Nullable Reference Types

Enable nullable reference types. Be explicit about nullability.

```csharp
public sealed record ExtendedBiddingConfig(
    bool Enabled,
    int TriggerWindowMinutes,
    int ExtensionDurationMinutes,
    int? MaxExtensions);  // Explicitly nullable — null means unlimited
```

### Message Record Field Nullability

Fields on commands, events, and integration messages that are always populated must be **required and non-nullable**. Optional-with-default is a code smell that hides type-system guarantees and invites construction-time omissions.

```csharp
// ❌ WRONG — implies SellerId might be absent; it never is
public sealed record ListingSold(
    Guid ListingId,
    Guid WinnerId,
    Guid? SellerId = null);

// ✅ CORRECT — compiler enforces population at all construction sites
public sealed record ListingSold(
    Guid ListingId,
    Guid WinnerId,
    Guid SellerId,
    decimal HammerPrice,
    DateTimeOffset SoldAt);
```

**Nullable fields ARE appropriate when:**
- The field is genuinely optional by business logic (`string? ReserveNotes`)
- The field was added in a later version for backward compatibility with existing serialized messages

---

## Pattern Matching

Use modern pattern matching over type checks and null guards:

```csharp
// Type patterns
if (result is BidAccepted accepted)
    yield return new BidPlacedNotification(accepted.ListingId, accepted.Amount, ...);

// Property patterns
if (listing is { Status: ListingStatus.Open, ReserveMet: false, CurrentHighBid: > 0 })
    return ListingDecision.ReservePotentiallyMet;

// Switch expressions
var displayStatus = listing.Status switch
{
    ListingStatus.Pending => "Upcoming",
    ListingStatus.Open => "Live",
    ListingStatus.Closed => "Ended",
    ListingStatus.Withdrawn => "Withdrawn",
    _ => "Unknown"
};
```

---

## Async/Await

Follow async conventions consistently:

```csharp
// ✅ CORRECT — async all the way, with cancellation
public static async Task<Listing?> Handle(GetListing query, IDocumentSession session, CancellationToken ct)
    => await session.LoadAsync<Listing>(query.ListingId, ct);

// ✅ CORRECT — return Task directly when no await needed
public static Task<Listing?> Handle(GetListing query, IDocumentSession session, CancellationToken ct)
    => session.LoadAsync<Listing>(query.ListingId, ct);

// ❌ WRONG — blocks async, deadlock risk
public static Listing? Handle(GetListing query, IDocumentSession session)
    => session.LoadAsync<Listing>(query.Id).Result;
```

---

## GUIDs

Use `Guid.CreateVersion7()` for new identifiers — time-ordered, better for database index performance:

```csharp
var listingId = Guid.CreateVersion7();
var bidId = Guid.CreateVersion7();
```

Reserve UUID v5 (deterministic, SHA-1) for natural-key aggregates where multiple handlers need to resolve the same stream ID without a database lookup. See `marten-event-sourcing.md`.

---

## DateTimeOffset

Always use `DateTimeOffset` instead of `DateTime`. Always UTC.

```csharp
// ✅ CORRECT
public DateTimeOffset PublishedAt { get; init; }
public DateTimeOffset? ClosedAt { get; init; }

var now = DateTimeOffset.UtcNow;

// ❌ WRONG — loses timezone information
public DateTime PublishedAt { get; init; }
```

---

## Decimal and Financial Calculations

### ⚠️ CRITICAL: Banker's Rounding

`Math.Round()` uses **banker's rounding** (round-to-even) by default, not round-away-from-zero.

```csharp
Math.Round(6.825m, 2)  // → 6.82 (rounds DOWN to even) — not 6.83!
Math.Round(6.835m, 2)  // → 6.84 (rounds UP to even)
Math.Round(4.5m, 0)    // → 4   (rounds DOWN to even)
Math.Round(5.5m, 0)    // → 6   (rounds UP to even)
```

This affects final value fee calculations, settlement splits, and any percentage-based math. Errors accumulate across bulk calculations and produce test failures when the expected value assumes traditional rounding.

**When you need round-away-from-zero, be explicit:**

```csharp
var fee = Math.Round(hammerPrice * feeRate, 2, MidpointRounding.AwayFromZero);
```

**Best practices:**
- Document the rounding mode in all financial calculation methods
- Test with midpoint values (X.X25, X.X75) to catch rounding assumptions
- Use the same rounding mode consistently across a calculation pipeline
- Check regulatory requirements for the jurisdiction — some mandate specific rules

---

## .NET 10 Gotcha: `Guid.Variant` and `Guid.Version`

In .NET 10, `System.Guid` gained `Variant` and `Version` as public instance properties. This breaks Marten's DCB `ValueTypeInfo` validation, which requires exactly one public instance property on tag type records. Raw `Guid` can no longer be used as a DCB tag type.

```csharp
// ❌ WRONG — Guid has 2 public instance properties in .NET 10
opts.Events.RegisterTagType<Guid>("listing");

// ✅ CORRECT — single-property wrapper record
public sealed record ListingStreamId(Guid Value);
opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<Listing>();
```

This is the same wrapper-record pattern used for value objects, but the motivation here is a framework constraint rather than domain modeling.

---

## Naming Conventions

| Type | Convention | Example |
|---|---|---|
| Commands | Verb + Noun | `PlaceBid`, `OpenBidding`, `PublishListing` |
| Queries | Get + Noun | `GetListing`, `GetBidHistory` |
| Domain events | Noun + Past Verb | `BidPlaced`, `BiddingOpened`, `ListingSold` |
| Integration messages | Noun + Past Verb | `ListingSold`, `SettlementCompleted` |
| Handlers | Command/Query + Handler | `PlaceBidHandler` |
| Validators | Command/Query + Validator | `PlaceBidValidator` |
| Aggregates | Domain noun | `Listing`, `ParticipantSession` |
| Sagas | Domain noun + Saga | `AuctionClosingSaga`, `ObligationsSaga` |

**Event naming rules specific to CritterBids:**
- No "Event" suffix — `BidPlaced` not `BidPlacedEvent`
- Past tense — `BiddingOpened` not `OpenBidding`
- Aggregate ID always the first property
- See `domain-event-conventions.md` for the full vocabulary reference
