namespace KeyLoad.Orleans;

/// <summary>Centrally configured signed peer freshness and bounded generation rediscovery.</summary>
[ConfigurationOptions]
public sealed class ReplicaTransportOptions
{
    /// <summary>The central transport execution configuration section.</summary>
    public const string SectionName = "KeyLoad:ReplicaTransport";
    /// <summary>The startup rejection for unsafe freshness or retry settings.</summary>
    public const string ValidationMessage = "Replica envelope lifetime must be positive and at most thirty seconds; transport attempts must be between one and two.";

    private const int MaximumLifetimeSeconds = 30;
    private const int MinimumAttempts = 1;
    private const int MaximumSafeAttempts = 2;

    /// <summary>Gets or sets the accepted signed peer clock skew and replay retention interval.</summary>
    public TimeSpan EnvelopeLifetime { get; set; } = TimeSpan.FromSeconds(MaximumLifetimeSeconds);
    /// <summary>Gets or sets the total RPC attempts, with at most one generation rediscovery.</summary>
    public int MaximumAttempts { get; set; } = MaximumSafeAttempts;

    /// <summary>Checks the accepted freshness and retry ceiling before native transport startup.</summary>
    /// <returns>Whether both values stay within the frozen safety bounds.</returns>
    public bool IsValid() => EnvelopeLifetime > TimeSpan.Zero
        && EnvelopeLifetime <= TimeSpan.FromSeconds(MaximumLifetimeSeconds)
        && MaximumAttempts is >= MinimumAttempts and <= MaximumSafeAttempts;
}
