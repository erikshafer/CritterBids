using CritterBids.Selling;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Pure-function tests for <see cref="ListingValidator"/>.
/// No framework, no host, no Testcontainers — all 14 rules are synchronous.
/// Mapping: scenarios 5.1–5.14 from <c>docs/workshops/004-scenarios.md</c> §5.
/// </summary>
public class ListingValidatorTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static CreateDraftListing ValidFlashDraft(
        string? title = "Hand-Forged Damascus Steel Knife",
        decimal startingBid = 50m,
        decimal? reservePrice = null,
        decimal? buyItNowPrice = null,
        TimeSpan? duration = null,
        bool extendedBiddingEnabled = false,
        TimeSpan? triggerWindow = null,
        TimeSpan? extension = null)
        => new(
            SellerId: Guid.CreateVersion7(),
            Title: title ?? string.Empty,
            Format: ListingFormat.Flash,
            StartingBid: startingBid,
            ReservePrice: reservePrice,
            BuyItNowPrice: buyItNowPrice,
            Duration: duration,
            ExtendedBiddingEnabled: extendedBiddingEnabled,
            ExtendedBiddingTriggerWindow: triggerWindow,
            ExtendedBiddingExtension: extension);

    // ── 5.1 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidDraft_Passes()
    {
        var result = ListingValidator.Validate(ValidFlashDraft());

        result.IsValid.ShouldBeTrue();
        result.IsRejection.ShouldBeFalse();
        result.Reason.ShouldBeNull();
    }

    // ── 5.2–5.4: Title ─────────────────────────────────────────────────────────

    [Fact]
    public void Title_Empty_IsRejected()
    {
        var draft = ValidFlashDraft(title: "");

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("Title is required");
    }

    [Fact]
    public void Title_Whitespace_IsRejected()
    {
        var draft = ValidFlashDraft(title: "   ");

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("Title cannot be empty");
    }

    [Fact]
    public void Title_ExceedsMaxLength_IsRejected()
    {
        var draft = ValidFlashDraft(title: new string('A', 201));

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("Title must be at most 200 characters");
    }

    // ── 5.5: Starting bid ──────────────────────────────────────────────────────

    [Fact]
    public void StartingBid_Zero_IsRejected()
    {
        var draft = ValidFlashDraft(startingBid: 0m);

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("StartingBid must be greater than zero");
    }

    // ── 5.6–5.10: Price ordering ───────────────────────────────────────────────

    [Fact]
    public void Reserve_BelowStartingBid_IsRejected()
    {
        var draft = ValidFlashDraft(startingBid: 50m, reservePrice: 40m);

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("ReservePrice must be >= StartingBid");
    }

    [Fact]
    public void BuyItNow_BelowReserve_IsRejected()
    {
        var draft = ValidFlashDraft(startingBid: 50m, reservePrice: 100m, buyItNowPrice: 75m);

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("BuyItNowPrice must be >= ReservePrice");
    }

    [Fact]
    public void BuyItNow_EqualsStartingBid_IsRejected()
    {
        var draft = ValidFlashDraft(startingBid: 50m, buyItNowPrice: 50m);

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("BuyItNowPrice must be greater than StartingBid");
    }

    [Fact]
    public void Reserve_Null_WithBuyItNow_IsValid()
    {
        // BIN >= Reserve is vacuously true when Reserve is null (scenario 5.9)
        var draft = ValidFlashDraft(reservePrice: null, buyItNowPrice: 200m);

        var result = ListingValidator.Validate(draft);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void BuyItNow_Null_IsValid()
    {
        var draft = ValidFlashDraft(reservePrice: 100m, buyItNowPrice: null);

        var result = ListingValidator.Validate(draft);

        result.IsValid.ShouldBeTrue();
    }

    // ── 5.11–5.12: Format / Duration ──────────────────────────────────────────

    [Fact]
    public void Flash_WithDuration_IsRejected()
    {
        var draft = ValidFlashDraft(duration: TimeSpan.FromMinutes(5));

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("Flash listings cannot specify a Duration");
    }

    [Fact]
    public void Timed_WithoutDuration_IsRejected()
    {
        var cmd = new CreateDraftListing(
            SellerId: Guid.CreateVersion7(),
            Title: "Test Item",
            Format: ListingFormat.Timed,
            StartingBid: 50m,
            ReservePrice: null,
            BuyItNowPrice: null,
            Duration: null,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null);

        var result = ListingValidator.Validate(cmd);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("Timed listings must specify a Duration");
    }

    // ── 5.13–5.14: Extended bidding ────────────────────────────────────────────

    [Fact]
    public void ExtendedBidding_TriggerWindowExceedsMax_IsRejected()
    {
        var draft = ValidFlashDraft(
            extendedBiddingEnabled: true,
            triggerWindow: TimeSpan.FromMinutes(5)); // exceeds 2-minute max

        var result = ListingValidator.Validate(draft);

        result.IsRejection.ShouldBeTrue();
        result.Reason.ShouldBe("ExtendedBiddingTriggerWindow must be <= 2 minutes");
    }

    [Fact]
    public void ExtendedBidding_Disabled_IgnoresInvalidWindow_IsValid()
    {
        // Disabled — trigger window is ignored even though it would fail the ceiling check
        var draft = ValidFlashDraft(
            extendedBiddingEnabled: false,
            triggerWindow: TimeSpan.FromHours(1));

        var result = ListingValidator.Validate(draft);

        result.IsValid.ShouldBeTrue();
    }
}
