namespace KeyLoad.Query.Search;

/// <summary>Closed execution snapshot byte and simultaneous restore reservation bounds.</summary>
[ConfigurationOptions]
public sealed record PackedAnnStorageOptions
{
    /// <summary>Configuration section for execution disposable ANN snapshots.</summary>
    public const string SectionName = "KeyLoad:PackedAnnStorage";
    /// <summary>Safe rejection for invalid bounded execution snapshot settings.</summary>
    public const string ValidationMessage = "The packed ANN snapshot bounds are invalid.";
    private const long DefaultFileBytes = 268_435_456;
    private const long DefaultPeakBytes = 536_870_912;
    private const long MinimumBytes = 1_024;
    private const long MaximumBytes = 8_589_934_592;

    /// <summary>Maximum complete framed execution payload bytes.</summary>
    public long MaxFileBytes { get; init; } = DefaultFileBytes;
    /// <summary>Maximum modeled simultaneously retained execution restore bytes.</summary>
    public long MaxPeakBytes { get; init; } = DefaultPeakBytes;

    /// <summary>Checks the closed finite execution snapshot limits.</summary>
    public bool IsValid() => MaxFileBytes is >= MinimumBytes and <= MaximumBytes
        && MaxPeakBytes is >= MinimumBytes and <= MaximumBytes;

    /// <summary>Rejects invalid finite disk and execution restore reservation settings.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw Errors.Fail(ErrorCode.Validation, ValidationMessage);
        }
    }
}
