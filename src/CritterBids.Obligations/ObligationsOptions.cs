namespace CritterBids.Obligations;

/// <summary>
/// Bound configuration for the Obligations BC's post-sale coordination timers. Resolves
/// W001-6 (demo-mode timeout config): the saga reads the reminder offset, ship-by deadline,
/// and auto-confirm window from this options section, which carries both production durations
/// and demo-mode durations selectable without recompilation.
///
/// <para><b>Why config, not <c>#if DEBUG</c> (W005 Decision 4 / M6-S1 §S1b).</b> The full
/// post-sale lifecycle must run live within a conference demo session (seconds), while
/// production durations are days. A <see cref="DemoMode"/> flag flips between the two
/// duration sets at runtime; integration tests inject short durations through this same path.
/// The saga's transitions are identical under either set — only the offsets differ.</para>
///
/// <para>BC-internal config, not a contract: it binds from appsettings and is consumed only
/// inside the Obligations BC, so it lives with the BC project rather than in
/// <c>CritterBids.Contracts</c> (which is integration-events-only by discipline).</para>
/// </summary>
public sealed record ObligationsOptions
{
    /// <summary>Configuration section name (<c>Obligations</c>) bound in <c>AddObligationsModule()</c>.</summary>
    public const string SectionName = "Obligations";

    /// <summary>When <c>true</c>, the saga uses <see cref="Demo"/> durations; otherwise <see cref="Production"/>.</summary>
    public bool DemoMode { get; set; }

    /// <summary>Real-world timer durations used outside demo mode.</summary>
    public ObligationsDurations Production { get; set; } = new()
    {
        ReminderOffset = TimeSpan.FromDays(2),
        ShipByDeadline = TimeSpan.FromDays(5),
        AutoConfirmWindow = TimeSpan.FromDays(3),
    };

    /// <summary>Collapsed-to-seconds durations so the full lifecycle runs live in a demo session.</summary>
    public ObligationsDurations Demo { get; set; } = new()
    {
        ReminderOffset = TimeSpan.FromSeconds(5),
        ShipByDeadline = TimeSpan.FromSeconds(10),
        AutoConfirmWindow = TimeSpan.FromSeconds(10),
    };

    /// <summary>The duration set currently in effect, selected by <see cref="DemoMode"/>.</summary>
    public ObligationsDurations Active => DemoMode ? Demo : Production;
}

/// <summary>
/// One set of post-sale coordination timer durations.
/// </summary>
public sealed record ObligationsDurations
{
    /// <summary>How long after saga start the single shipping reminder fires (before the deadline).</summary>
    public TimeSpan ReminderOffset { get; set; }

    /// <summary>How long after saga start the seller's ship-by deadline falls; missing it escalates.</summary>
    public TimeSpan ShipByDeadline { get; set; }

    /// <summary>How long after tracking is provided that delivery auto-confirms.</summary>
    public TimeSpan AutoConfirmWindow { get; set; }
}
