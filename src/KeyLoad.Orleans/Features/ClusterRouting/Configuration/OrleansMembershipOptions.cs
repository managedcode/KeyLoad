namespace KeyLoad.Orleans;

/// <summary>Centrally validated scheduling, admission and replay settings for native membership ownership.</summary>
[ConfigurationOptions]
public sealed class OrleansMembershipOptions
{
    /// <summary>The section used by the central server configuration registry.</summary>
    public const string SectionName = "KeyLoad:OrleansMembership";
    /// <summary>The startup rejection for settings outside the supported membership bounds.</summary>
    public const string ValidationMessage = "Orleans membership scheduling, admission and replay settings exceed their accepted bounds.";

    private const int MaximumRequestBytesCeiling = 65_536;
    private const int MaximumReplyBytesCeiling = 262_144;
    private const int MaximumSnapshotBytesCeiling = 262_144;
    private const int MaximumRowBytesCeiling = 4_096;
    private const int MaximumRowsCeiling = 48;
    private const int MaximumSuspectsCeiling = 6;
    private const int MaximumAddressBytesCeiling = 256;
    private const int MaximumHeaderBytesCeiling = 4_096;
    private const int MaximumHeaderValueBytesCeiling = 512;
    private const int AuthenticationScratchBytesCeiling = 512;
    private const int MaximumResolvedAddressesCeiling = 8;
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
    private static readonly TimeSpan MaximumStartupTimeout = TimeSpan.FromMinutes(MaximumStartupMinutes);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(MaximumIntervalMinutes);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromSeconds(MaximumClockSkewSeconds);
    private static readonly TimeSpan MaximumShutdownTimeout = TimeSpan.FromSeconds(DefaultStartupSeconds);

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

    /// <summary>Gets or sets the bounded request bytes.</summary>
    public int MaximumRequestBytes { get; set; } = MaximumRequestBytesCeiling;
    /// <summary>Gets or sets the bounded reply bytes.</summary>
    public int MaximumReplyBytes { get; set; } = MaximumReplyBytesCeiling;
    /// <summary>Gets or sets the bounded persisted snapshot bytes.</summary>
    public int MaximumSnapshotBytes { get; set; } = MaximumSnapshotBytesCeiling;
    /// <summary>Gets or sets the bounded native row bytes.</summary>
    public int MaximumRowBytes { get; set; } = MaximumRowBytesCeiling;
    /// <summary>Gets or sets the bounded authority rows.</summary>
    public int MaximumRows { get; set; } = MaximumRowsCeiling;
    /// <summary>Gets or sets the bounded suspects per row.</summary>
    public int MaximumSuspects { get; set; } = MaximumSuspectsCeiling;
    /// <summary>Gets or sets the bounded identity and address bytes.</summary>
    public int MaximumAddressBytes { get; set; } = MaximumAddressBytesCeiling;
    /// <summary>Gets or sets the bounded signed header bytes.</summary>
    public int MaximumHeaderBytes { get; set; } = MaximumHeaderBytesCeiling;
    /// <summary>Gets or sets the bounded one signed header value.</summary>
    public int MaximumHeaderValueBytes { get; set; } = MaximumHeaderValueBytesCeiling;
    /// <summary>Gets or sets the bounded initial authentication scratch buffer.</summary>
    public int AuthenticationScratchBytes { get; set; } = AuthenticationScratchBytesCeiling;
    /// <summary>Gets or sets the bounded resolved addresses per configured voter.</summary>
    public int MaximumResolvedAddresses { get; set; } = MaximumResolvedAddressesCeiling;

    /// <summary>Checks startup bounds without changing membership or persisted state.</summary>
    /// <returns>Whether every duration and capacity satisfies the frozen safety bounds.</returns>
    public bool IsValid() => PositiveAtMost(StartupTimeout, MaximumStartupTimeout)
        && PositiveAtMost(StartupRetryDelay, MaximumInterval)
        && StartupRetryDelay <= StartupTimeout
        && PositiveAtMost(ConnectTimeout, MaximumInterval)
        && HeartbeatAttempts is >= MinimumPositiveCount and <= MaximumHeartbeatAttempts
        && MaximumAdmissions is >= MinimumPositiveCount and <= MaximumAdmittedRequests
        && PositiveAtMost(ClockSkew, MaximumClockSkew)
        && PositiveAtMost(ReplayLifetime, MaximumStartupTimeout)
        && ReplayLifetime.Ticks >= ClockSkew.Ticks * ReplaySkewWindows
        && ReplayNonceCapacity is >= MinimumPositiveCount and <= MaximumRetainedNonces
        && PositiveAtMost(MembershipRefresh, MaximumInterval)
        && PositiveAtMost(ShutdownTimeout, MaximumShutdownTimeout)
        && MaximumRequestBytes is >= MinimumPositiveCount and <= MaximumRequestBytesCeiling
        && MaximumReplyBytes is >= MinimumPositiveCount and <= MaximumReplyBytesCeiling
        && MaximumSnapshotBytes is >= MinimumPositiveCount and <= MaximumSnapshotBytesCeiling
        && MaximumRowBytes is >= MinimumPositiveCount and <= MaximumRowBytesCeiling
        && MaximumRows is >= MinimumPositiveCount and <= MaximumRowsCeiling
        && MaximumSuspects is >= MinimumPositiveCount and <= MaximumSuspectsCeiling
        && MaximumAddressBytes is >= MinimumPositiveCount and <= MaximumAddressBytesCeiling
        && MaximumHeaderBytes is >= MinimumPositiveCount and <= MaximumHeaderBytesCeiling
        && MaximumHeaderValueBytes is >= MinimumPositiveCount and <= MaximumHeaderValueBytesCeiling
        && AuthenticationScratchBytes is >= MinimumPositiveCount and <= AuthenticationScratchBytesCeiling
        && MaximumResolvedAddresses is >= MinimumPositiveCount and <= MaximumResolvedAddressesCeiling
        && MaximumRowBytes <= MaximumSnapshotBytes && MaximumSnapshotBytes <= MaximumReplyBytes
        && MaximumHeaderValueBytes <= MaximumHeaderBytes;

    /// <summary>Rejects invalid native membership settings before execution starts.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new Microsoft.Extensions.Options.OptionsValidationException(SectionName, typeof(OrleansMembershipOptions), [ValidationMessage]); }
    }

    private static bool PositiveAtMost(TimeSpan duration, TimeSpan maximum)
        => duration > TimeSpan.Zero && duration <= maximum;
}
