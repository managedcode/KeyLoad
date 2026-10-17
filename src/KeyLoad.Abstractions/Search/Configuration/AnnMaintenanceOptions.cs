namespace KeyLoad;

/// <summary>Finite page admission for one original administrator maintenance request.</summary>
[ConfigurationOptions]
public sealed record AnnMaintenanceOptions
{
    /// <summary>Central native parent configuration section.</summary>
    public const string SectionName = "KeyLoad:AnnMaintenance";
    /// <summary>Safe invalid configuration detail.</summary>
    public const string ValidationMessage = "The ANN maintenance page bound is invalid.";
    private const int DefaultPages = 32;
    private const int MaximumPages = 64;
    private const int MinimumPages = 1;
    /// <summary>Maximum actual page/checkpoint operations before this request stops with BudgetExceeded.</summary>
    public int MaximumReplayPages { get; init; } = DefaultPages;
    /// <summary>Validates the closed bounded native parent page count.</summary>
    public bool IsValid() => MaximumReplayPages is >= MinimumPages and <= MaximumPages;
}
