namespace KeyLoad.Orleans;

/// <summary>Centrally validated scheduling, admission and replay settings for native membership ownership.</summary>
[ConfigurationOptions]
public sealed class OrleansMembershipOptions
{
    /// <summary>The section used by the central server configuration registry.</summary>
    public const string SectionName = "KeyLoad:OrleansMembership";
    /// <summary>The startup rejection for settings outside the supported membership bounds.</summary>
    public const string ValidationMessage = "Orleans membership scheduling, admission and replay settings exceed their accepted bounds.";

    private const int DefaultStartupSeconds = 120;
    private const int DefaultRetryMilliseconds = 250;
    private const int DefaultConnectMilliseconds = 500;
    private const int MaximumHeartbeatAttempts = 16;
    private const int MaximumAdmittedRequests = 8;
    private const int MaximumClockSkewSeconds = 30;
    private const int DefaultReplayMinutes = 2;
    private const int MaximumRetainedNonces = 16_384;
    private const int MaximumStartupMinutes = 10;
    private const int MaximumIntervalMinutes = 1;
    private const int MinimumPositiveCount = 1;
    private const int ReplaySkewWindows = 2;
    private const int DefaultMembershipRefreshSeconds = 5;
    private const int DefaultShutdownSeconds = 30;

    /// <summary>Gets or sets the bounded membership initialization lifetime.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(DefaultStartupSeconds);
    /// <summary>Gets or sets the delay between transient initialization failures.</summary>
    public TimeSpan StartupRetryDelay { get; set; } = TimeSpan.FromMilliseconds(DefaultRetryMilliseconds);
    /// <summary>Gets or sets the maximum heartbeat compare-exchange attempts.</summary>
    public int HeartbeatAttempts { get; set; } = MaximumHeartbeatAttempts;
    /// <summary>Gets or sets the maximum simultaneously admitted authority exchanges.</summary>
    public int MaximumAdmissions { get; set; } = MaximumAdmittedRequests;
    /// <summary>Gets or sets the connection timeout for one authority HTTP endpoint.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(DefaultConnectMilliseconds);
    /// <summary>Gets or sets the maximum clock skew accepted for signed authority exchanges.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(MaximumClockSkewSeconds);
    /// <summary>Gets or sets the lifetime of retained authority replay nonces.</summary>
    public TimeSpan ReplayLifetime { get; set; } = TimeSpan.FromMinutes(DefaultReplayMinutes);
    /// <summary>Gets or sets the maximum retained authority replay nonces.</summary>
    public int ReplayNonceCapacity { get; set; } = MaximumRetainedNonces;
    /// <summary>Gets or sets the native membership heartbeat and table refresh interval.</summary>
    public TimeSpan MembershipRefresh { get; set; } = TimeSpan.FromSeconds(DefaultMembershipRefreshSeconds);
    /// <summary>Gets or sets the cancellation bound for native silo shutdown observation.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(DefaultShutdownSeconds);

    /// <summary>Checks startup bounds without changing membership or persisted state.</summary>
    /// <returns>Whether every duration and capacity satisfies the frozen safety bounds.</returns>
    public bool IsValid() => PositiveAtMost(StartupTimeout, TimeSpan.FromMinutes(MaximumStartupMinutes))
        && PositiveAtMost(StartupRetryDelay, TimeSpan.FromMinutes(MaximumIntervalMinutes))
        && StartupRetryDelay <= StartupTimeout
        && PositiveAtMost(ConnectTimeout, TimeSpan.FromMinutes(MaximumIntervalMinutes))
        && HeartbeatAttempts is >= MinimumPositiveCount and <= MaximumHeartbeatAttempts
        && MaximumAdmissions is >= MinimumPositiveCount and <= MaximumAdmittedRequests
        && PositiveAtMost(ClockSkew, TimeSpan.FromSeconds(MaximumClockSkewSeconds))
        && PositiveAtMost(ReplayLifetime, TimeSpan.FromMinutes(MaximumStartupMinutes))
        && ReplayLifetime.Ticks >= ClockSkew.Ticks * ReplaySkewWindows
        && ReplayNonceCapacity is >= MinimumPositiveCount and <= MaximumRetainedNonces
        && PositiveAtMost(MembershipRefresh, TimeSpan.FromMinutes(MaximumIntervalMinutes))
        && PositiveAtMost(ShutdownTimeout, TimeSpan.FromSeconds(DefaultStartupSeconds));

    private static bool PositiveAtMost(TimeSpan duration, TimeSpan maximum)
        => duration > TimeSpan.Zero && duration <= maximum;
}
