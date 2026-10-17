namespace KeyLoad;

/// <summary>Configures event retention bounds.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventRetentionPolicy)]
public sealed record EventRetentionPolicy
{
    private const int DefaultMaxEvents = 100_000;
    private const int DefaultMaxBytes = 1_073_741_824;

    /// <summary>Gets or sets the maximum number of retained events.</summary>
    [Orleans.Id(0)]
    public long MaxEvents { get; init; } = DefaultMaxEvents;
    /// <summary>Gets or sets the maximum number of retained event bytes.</summary>
    [Orleans.Id(1)]
    public long MaxBytes { get; init; } = DefaultMaxBytes;
}
