namespace KeyLoad.Replication;

/// <summary>Centrally configured deadlines and ordered apply budgets for a physical replica owner.</summary>
[ConfigurationOptions]
public sealed record ReplicaExecutionOptions
{
    /// <summary>The section bound by the server and silo composition roots.</summary>
    public const string SectionName = "KeyLoad:ReplicationExecution";

    private const int DefaultCommandTimeoutSeconds = 20;
    private const int DefaultReadBarrierTimeoutSeconds = 10;
    private const int DefaultApplyBatchSize = 64;
    private const int MinimumApplyBatchSize = 1;
    private const int DefaultMaximumSnapshotChunksPerRound = 4;
    private const int DefaultFileBufferBytes = 65_536;
    private const int DefaultMaximumManifestBytes = 4_096;
    private const int MinimumResourceBudget = 1;
    private const int MaximumFileBufferBytes = 65_536;
    private const int MaximumManifestEncodingBytes = 4_096;
    private const long MaximumTimerDelayMilliseconds = 4_294_967_294;
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromMilliseconds(MaximumTimerDelayMilliseconds);

    /// <summary>Gets the maximum foreground command and protocol request deadline.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(DefaultCommandTimeoutSeconds);

    /// <summary>Gets the maximum current-term quorum read deadline.</summary>
    public TimeSpan ReadBarrierTimeout { get; init; } = TimeSpan.FromSeconds(DefaultReadBarrierTimeoutSeconds);

    /// <summary>Gets the maximum committed entries applied before publishing progress and yielding the apply gate.</summary>
    public int ApplyBatchSize { get; init; } = DefaultApplyBatchSize;

    /// <summary>Gets the maximum snapshot chunks sent to one follower before the maintenance round yields.</summary>
    public int MaximumSnapshotChunksPerRound { get; init; } = DefaultMaximumSnapshotChunksPerRound;

    /// <summary>Gets the native private-file and offline-digest IO buffer size.</summary>
    public int FileBufferBytes { get; init; } = DefaultFileBufferBytes;

    /// <summary>Gets the admitted incoming snapshot descriptor bytes within the existing encoding safety bound.</summary>
    public int MaximumManifestBytes { get; init; } = DefaultMaximumManifestBytes;

    /// <summary>Checks positive budgets and native timer bounds, including the composed membership request deadline.</summary>
    /// <returns>Whether all configured execution settings can be used by their native execution APIs.</returns>
    public bool IsValid() => CommandTimeout > TimeSpan.Zero && CommandTimeout <= MaximumTimerDelay
        && ReadBarrierTimeout > TimeSpan.Zero && ReadBarrierTimeout <= MaximumTimerDelay
        && CommandTimeout + ReadBarrierTimeout <= MaximumTimerDelay && ApplyBatchSize >= MinimumApplyBatchSize
        && MaximumSnapshotChunksPerRound >= MinimumResourceBudget
        && FileBufferBytes >= MinimumResourceBudget && FileBufferBytes <= MaximumFileBufferBytes
        && MaximumManifestBytes >= MinimumResourceBudget && MaximumManifestBytes <= MaximumManifestEncodingBytes;

    /// <summary>Rejects invalid settings before replica recovery or admitted execution begins.</summary>
    /// <exception cref="InvalidOperationException">A deadline or apply budget is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ReplicaProtocol.InvalidLimits);
        }
    }
}
