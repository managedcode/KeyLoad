namespace KeyLoad;

/// <summary>Centrally configured finite receiver-monotonic cache eligibility, independent of database read authority.</summary>
[ConfigurationOptions]
public sealed record CacheReadPermitOptions
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:CacheReadPermit";
    /// <summary>The startup rejection for invalid cache eligibility durations.</summary>
    public const string ValidationMessage = "Cache prepare and lease validity must be positive, ordered and at most one minute.";
    private const int DefaultPrepareValiditySeconds = 10;
    private const int DefaultLeaseValiditySeconds = 15;
    private const int MaximumLeaseValidityMinutes = 1;
    private static readonly TimeSpan MaximumLeaseValidity = TimeSpan.FromMinutes(MaximumLeaseValidityMinutes);

    /// <summary>Gets the maximum prepare age at acceptance; the exact boundary is rejected.</summary>
    public TimeSpan PrepareValidity { get; init; } = TimeSpan.FromSeconds(DefaultPrepareValiditySeconds);
    /// <summary>Gets the maximum lease age from receiver preparation; the exact boundary is expired.</summary>
    public TimeSpan LeaseValidity { get; init; } = TimeSpan.FromSeconds(DefaultLeaseValiditySeconds);

    /// <summary>Checks that both durations remain finite, positive and ordered.</summary>
    /// <returns>Whether the configured eligibility policy is valid.</returns>
    public bool IsValid() => PrepareValidity > TimeSpan.Zero && PrepareValidity <= LeaseValidity
        && LeaseValidity <= MaximumLeaseValidity;

    /// <summary>Rejects invalid policy before a cache permit is exposed.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
