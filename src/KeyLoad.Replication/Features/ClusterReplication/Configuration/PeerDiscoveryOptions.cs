namespace KeyLoad.Replication;

/// <summary>Centrally configured transport and replay policy for authenticated bodyless peer discovery.</summary>
[ConfigurationOptions]
public sealed record PeerDiscoveryOptions
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:PeerDiscovery";
    /// <summary>The startup rejection for invalid transport and replay settings.</summary>
    public const string ValidationMessage = PeerDiscoveryProtocol.InvalidConfiguration;
    private const int DefaultReplayCapacity = 8_192;
    private const int DefaultConnectTimeoutMilliseconds = 500;
    private const int DefaultPooledConnectionLifetimeMinutes = 5;
    private const int DefaultTimestampWindowSeconds = 30;
    private const int MinimumReplayCapacity = 1;
    private const long MinimumTimestampWindowMilliseconds = 1;
    private const long WholeMillisecondRemainder = 0;
    private static readonly TimeSpan MaximumTransportDuration = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>Gets the positive bounded socket connection deadline.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(DefaultConnectTimeoutMilliseconds);
    /// <summary>Gets the finite lifetime of a pooled discovery connection.</summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(DefaultPooledConnectionLifetimeMinutes);
    /// <summary>Gets the accepted timestamp skew and the nonce expiry interval in whole milliseconds.</summary>
    public TimeSpan TimestampWindow { get; set; } = TimeSpan.FromSeconds(DefaultTimestampWindowSeconds);
    /// <summary>Gets the maximum simultaneously retained authenticated discovery nonces.</summary>
    public int ReplayCapacity { get; set; } = DefaultReplayCapacity;

    /// <summary>Checks finite native transport durations, whole-millisecond freshness and positive replay capacity.</summary>
    /// <returns>Whether all configured discovery settings are valid.</returns>
    public bool IsValid() => ConnectTimeout > TimeSpan.Zero && ConnectTimeout <= MaximumTransportDuration
        && PooledConnectionLifetime > TimeSpan.Zero && PooledConnectionLifetime <= MaximumTransportDuration
        && TimestampWindow.Ticks >= MinimumTimestampWindowMilliseconds * TimeSpan.TicksPerMillisecond
        && TimestampWindow <= MaximumTransportDuration
        && TimestampWindow.Ticks % TimeSpan.TicksPerMillisecond == WholeMillisecondRemainder
        && ReplayCapacity >= MinimumReplayCapacity;

    /// <summary>Rejects invalid settings before authenticated transport or replay admission starts.</summary>
    /// <exception cref="KeyLoadException">Discovery transport or replay policy is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw Errors.Fail(ErrorCode.Validation, PeerDiscoveryProtocol.InvalidConfiguration);
        }
    }
}
