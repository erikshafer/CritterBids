namespace CritterBids.Selling;

/// <summary>
/// Result of <see cref="ListingValidator.Validate"/>. Immutable — construct via factory methods.
/// </summary>
public sealed record ValidationResult
{
    public bool IsValid { get; private init; }
    public bool IsRejection => !IsValid;
    public string? Reason { get; private init; }

    public static ValidationResult Valid() => new() { IsValid = true };
    public static ValidationResult Rejected(string reason) => new() { IsValid = false, Reason = reason };
}

/// <summary>
/// Pure-function validator for <see cref="CreateDraftListing"/> commands.
/// No framework, no host, no database — all 14 rules are synchronous and deterministic.
/// Called by <c>SubmitListingHandler</c> in S6 before publishing. Authored in S5 so the
/// rule set is independently testable before the submission flow is implemented.
/// </summary>
public static class ListingValidator
{
    private const int MaxTitleLength = 200;
    private static readonly TimeSpan MaxExtendedBiddingTriggerWindow = TimeSpan.FromMinutes(2);

    public static ValidationResult Validate(CreateDraftListing cmd)
    {
        // Rules 5.2–5.4: Title
        if (cmd.Title.Length == 0)
            return ValidationResult.Rejected("Title is required");

        if (string.IsNullOrWhiteSpace(cmd.Title))
            return ValidationResult.Rejected("Title cannot be empty");

        if (cmd.Title.Length > MaxTitleLength)
            return ValidationResult.Rejected("Title must be at most 200 characters");

        // Rule 5.5: Starting bid
        if (cmd.StartingBid <= 0m)
            return ValidationResult.Rejected("StartingBid must be greater than zero");

        // Rule 5.6: Reserve vs starting bid
        if (cmd.ReservePrice.HasValue && cmd.ReservePrice.Value < cmd.StartingBid)
            return ValidationResult.Rejected("ReservePrice must be >= StartingBid");

        // Rule 5.7: BIN vs reserve (vacuously valid when reserve is null — rule 5.9)
        if (cmd.BuyItNowPrice.HasValue && cmd.ReservePrice.HasValue
            && cmd.BuyItNowPrice.Value < cmd.ReservePrice.Value)
            return ValidationResult.Rejected("BuyItNowPrice must be >= ReservePrice");

        // Rule 5.8: BIN must be strictly greater than starting bid
        if (cmd.BuyItNowPrice.HasValue && cmd.BuyItNowPrice.Value == cmd.StartingBid)
            return ValidationResult.Rejected("BuyItNowPrice must be greater than StartingBid");

        // Rule 5.11: Flash requires null Duration
        if (cmd.Format == ListingFormat.Flash && cmd.Duration.HasValue)
            return ValidationResult.Rejected("Flash listings cannot specify a Duration");

        // Rule 5.12: Timed requires non-null Duration
        if (cmd.Format == ListingFormat.Timed && !cmd.Duration.HasValue)
            return ValidationResult.Rejected("Timed listings must specify a Duration");

        // Rule 5.13: Extended bidding trigger window ceiling (only when enabled — rule 5.14)
        if (cmd.ExtendedBiddingEnabled
            && cmd.ExtendedBiddingTriggerWindow.HasValue
            && cmd.ExtendedBiddingTriggerWindow.Value > MaxExtendedBiddingTriggerWindow)
            return ValidationResult.Rejected("ExtendedBiddingTriggerWindow must be <= 2 minutes");

        return ValidationResult.Valid();
    }
}
