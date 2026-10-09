namespace KeyLoad.Core;

/// <summary>Dedicated canonical parent admission bounds; defaults are provisional policy, not measured scale.</summary>
[ConfigurationOptions]
public sealed record PartitionMovementCheckpointOptions
{
    /// <summary>Gets the central node execution configuration section.</summary>
    public const string SectionName = "KeyLoad:PartitionMovementCheckpoint";
    /// <summary>Gets a payload-free configuration failure.</summary>
    public const string ValidationMessage = "Partition movement checkpoint bounds exceed the supported policy.";
    private const int EmptyQuota = 0;
    private const int PrincipalCeiling = 4;
    private const int DatabaseCeiling = 32;
    private const int PhaseCeiling = 4096;
    private const int BytesCeiling = 16 * 1024 * 1024;
    private const int TerminalCeiling = 4096;

    /// <summary>Gets admitted active moves for one persisted principal.</summary>
    public int MaxActiveMovesPerPrincipal { get; init; } = PrincipalCeiling;
    /// <summary>Gets admitted active moves for one database.</summary>
    public int MaxActiveMovesPerDatabase { get; init; } = DatabaseCeiling;
    /// <summary>Gets original phase identities retained by one active move.</summary>
    public int MaxPhaseRecordsPerMove { get; init; } = PhaseCeiling;
    /// <summary>Gets actual encoded metadata bytes retained by one active move.</summary>
    public long MaxRetainedMetadataBytesPerMove { get; init; } = BytesCeiling;
    /// <summary>Gets terminal move authorities retained by one database.</summary>
    public int MaxRetainedTerminalMovesPerDatabase { get; init; } = TerminalCeiling;
    /// <summary>Gets actual encoded terminal replay authority bytes retained by one database.</summary>
    public long MaxRetainedTerminalBytesPerDatabase { get; init; } = BytesCeiling;

    /// <summary>Validates dedicated durable quotas before native ownership admission.</summary>
    public bool IsValid() => MaxActiveMovesPerPrincipal > EmptyQuota && MaxActiveMovesPerPrincipal <= PrincipalCeiling
        && MaxActiveMovesPerDatabase >= MaxActiveMovesPerPrincipal && MaxActiveMovesPerDatabase <= DatabaseCeiling
        && MaxPhaseRecordsPerMove > EmptyQuota && MaxPhaseRecordsPerMove <= PhaseCeiling
        && MaxRetainedMetadataBytesPerMove > EmptyQuota && MaxRetainedMetadataBytesPerMove <= BytesCeiling
        && MaxRetainedTerminalMovesPerDatabase > EmptyQuota && MaxRetainedTerminalMovesPerDatabase <= TerminalCeiling
        && MaxRetainedTerminalBytesPerDatabase > EmptyQuota && MaxRetainedTerminalBytesPerDatabase <= BytesCeiling;

    /// <summary>Rejects invalid durable admission before startup or effect execution.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new InvalidOperationException(ValidationMessage); }
    }
}
