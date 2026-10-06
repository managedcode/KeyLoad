namespace KeyLoad.Core;

/// <summary>Coordinates direct embedded use of one real database engine.</summary>
/// <param name="database">Engine whose atomic store applies commands.</param>
public sealed class EmbeddedCoordinator(DatabaseEngine database) : ICommitCoordinator
{
    /// <inheritdoc />
    public Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.Apply(new(id, kind, principalId, database.EvaluationClock.GetUtcNow(), payloadJson)));
    }

    /// <inheritdoc />
    public Task<OperationResult> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.Apply(database.CreateNativeOperation(kind, id, principalId,
            database.EvaluationClock.GetUtcNow(), payload)));
    }

    /// <inheritdoc />
    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
