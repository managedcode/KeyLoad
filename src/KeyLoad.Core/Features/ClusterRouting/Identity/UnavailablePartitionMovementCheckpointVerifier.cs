namespace KeyLoad.Core;

/// <summary>Explicitly rejects movement authority in standalone owners without configured native peer authentication.</summary>
public sealed class UnavailablePartitionMovementCheckpointVerifier : IPartitionMovementCheckpointVerifier
{
    private const string UnavailableProofAuthority = "Native partition movement proof authority is unavailable in this execution composition.";
    /// <summary>Gets the immutable rejecting authority used by standalone execution compositions.</summary>
    public static UnavailablePartitionMovementCheckpointVerifier Instance { get; } = new();

    private UnavailablePartitionMovementCheckpointVerifier() { }

    /// <inheritdoc />
    public string Verify(DatabaseEngine database, string principalId, ReadOnlyMemory<byte> ownedBody,
        ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public ReadOnlyMemory<byte> VerifyTransferRead(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public ReadOnlyMemory<byte> VerifyTransferCleanup(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public string VerifyReceiverIssue(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public void RequireReceiverAdministrator(DatabaseEngine database, string principalId,
        ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public string VerifyReceiverEffect(DatabaseEngine database, string principalId, Guid originalCommandId,
        ReadOnlyMemory<byte> ownedEnvelope, ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public void RequireReceiverIssueCapacity(ReadOnlyMemory<byte> ownedNativeContext) => throw Unavailable();

    /// <inheritdoc />
    public string VerifyRetireCancellation(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work) => throw Unavailable();

    /// <inheritdoc />
    public string VerifyRetireCancellationQuery(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work) => throw Unavailable();

    private static KeyLoadException Unavailable() => Errors.Fail(ErrorCode.RecoveryRequired, UnavailableProofAuthority);
}
