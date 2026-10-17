namespace KeyLoad;

/// <summary>Bounds actual page/checkpoint children of one original text maintenance parent.</summary>
[ConfigurationOptions]
public sealed record TextIndexMaintenanceOptions
{
    /// <summary>Central feature-owned admission section.</summary>
    public const string SectionName = "KeyLoad:TextIndexMaintenance";
    /// <summary>Safe startup rejection detail.</summary>
    public const string ValidationMessage = "The text index maintenance page bound is invalid.";
    private const int DefaultPages = 32;
    private const int MaximumPages = 64;
    private const int MinimumPages = 1;

    /// <summary>Maximum actual native page/ACK stages before bounded rejection.</summary>
    public int MaximumReplayPages { get; init; } = DefaultPages;

    /// <summary>Checks the finite feature ceiling before physical ownership.</summary>
    public bool IsValid() => MaximumReplayPages is >= MinimumPages and <= MaximumPages;
}
