namespace KeyLoad.Core;

/// <summary>Uses configured native peer authority before Core may seal a complete movement checkpoint body.</summary>
public interface IPartitionMovementCheckpointVerifier
{
    /// <summary>Verifies the exact owned body against persisted original admission and returns its complete SHA256 digest.</summary>
    /// <param name="database">Current physical owner's engine; no retained engine or cached authority is permitted.</param>
    /// <param name="principalId">Freshly authenticated persisted operator subject.</param>
    /// <param name="ownedBody">Private native body snapshot, including any original raw Capture witness.</param>
    /// <param name="work">Current admitted native operation's bounded read work.</param>
    /// <returns>The digest of the complete verified native body.</returns>
    string Verify(DatabaseEngine database, string principalId, ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work);

    /// <summary>Verifies a fresh control read's exact native MAC proof, returning its bounded canonical authority bytes.</summary>
    /// <param name="database">Current physical source engine; original source outcome is checked independently by Core.</param>
    /// <param name="principalId">Fresh persisted administrator subject used by the receiving native read grain.</param>
    /// <param name="originalReply">Owned complete original control read reply bytes.</param>
    /// <param name="signature">The original distinct-purpose native MAC signature.</param>
    /// <param name="work">Current admitted native operation's bounded read work.</param>
    /// <returns>The complete native serialized canonical control read authority.</returns>
    ReadOnlyMemory<byte> VerifyTransferRead(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work);

    /// <summary>Authenticates historical transfer-read scope solely for joined session cleanup, never for reading data.</summary>
    /// <param name="database">Current physical source engine.</param>
    /// <param name="principalId">Fresh persisted administrator subject.</param>
    /// <param name="originalReply">Owned complete original control read reply bytes.</param>
    /// <param name="signature">Original distinct-purpose native MAC signature.</param>
    /// <param name="work">Fresh bounded native cleanup read work.</param>
    /// <returns>The complete native serialized historical authority for this cleanup scope.</returns>
    ReadOnlyMemory<byte> VerifyTransferCleanup(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work);

    /// <summary>Verifies the actual MAC-bound source pending scope before native receiver first issuance.</summary>
    /// <param name="database">Current receiver owner engine.</param>
    /// <param name="principalId">Fresh current receiver administrator subject.</param>
    /// <param name="ownedBody">Complete Core-enriched first issuance body including actual source proof.</param>
    /// <param name="work">Same admitted bounded native work.</param>
    /// <returns>Complete verified body digest.</returns>
    string VerifyReceiverIssue(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work);

    /// <summary>Authenticates the complete parent effect envelope and both actual native proof ACKs.</summary>
    /// <param name="database">Current actual receiving owner.</param>
    /// <param name="principalId">Fresh canonical receiver administrator.</param>
    /// <param name="originalCommandId">Exact original first-issued effect identity.</param>
    /// <param name="ownedEnvelope">Complete immutable native envelope including independent raw proofs.</param>
    /// <param name="work">Current bounded native operation work.</param>
    /// <returns>Digest of all verified envelope bytes.</returns>
    string VerifyReceiverEffect(DatabaseEngine database, string principalId, Guid originalCommandId,
        ReadOnlyMemory<byte> ownedEnvelope, ReadExecutionBudget work);

    /// <summary>Authenticates the native canonical receiver technical subject before any issuance metadata.</summary>
    /// <param name="database">Current actual receiver database owner.</param>
    /// <param name="principalId">Current authenticated subject, never a caller-selected expected identity.</param>
    /// <param name="work">Actual bounded receiver work.</param>
    void RequireReceiverAdministrator(DatabaseEngine database, string principalId, ReadExecutionBudget work);

    /// <summary>Checks complete native transport/checkpoint size from same-transaction facts, returning no authority.</summary>
    /// <param name="ownedNativeContext">Owned generated Core sizing context containing actual native rows and validated caps.</param>
    void RequireReceiverIssueCapacity(ReadOnlyMemory<byte> ownedNativeContext);

    /// <summary>Verifies dedicated expired-Retire cancellation authority against original immutable native scope.</summary>
    /// <param name="database">Actual current original source owner.</param>
    /// <param name="principalId">Fresh persisted receiver administrator.</param>
    /// <param name="ownedBody">Complete native cancellation body including fresh independently signed read proof.</param>
    /// <param name="work">Actual admitted bounded native work.</param>
    /// <returns>The complete verified owned-body digest.</returns>
    string VerifyRetireCancellation(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work);

    /// <summary>Verifies observation-only cancellation query authority without renewing the original effect.</summary>
    /// <param name="database">Actual current original source owner.</param>
    /// <param name="principalId">Fresh persisted receiver administrator.</param>
    /// <param name="ownedBody">Complete bounded native cancellation outcome-query body.</param>
    /// <param name="work">Actual admitted bounded native read work.</param>
    /// <returns>The complete verified owned-body digest.</returns>
    string VerifyRetireCancellationQuery(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work);

}
