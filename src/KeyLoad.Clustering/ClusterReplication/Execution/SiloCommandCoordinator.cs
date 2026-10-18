using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Submits authorized native operations to Orleans; it owns no cluster lifecycle or protocol.</summary>
public sealed class SiloCommandCoordinator(DatabaseEngine database, IGrainFactory grains) : ICommitCoordinator
{
    private IDatabaseCommitGrain Owner => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(database.Store.Identity.Incarnation));

    /// <inheritdoc />
    public Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitOperationAsync(database.NormalizeOperation(new(id, kind, principalId, default, payloadJson)), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return SubmitOperationAsync(database.CreateNativeOperation(kind, id, principalId, default, payload), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SubmitVerifiedAsync(ReplicatedOperation operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return SubmitOperationAsync(database.VerifyOperationAuthority(operation), cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReadBarrierAsync(CancellationToken cancellationToken = default)
        => _ = await Owner.ReadCutAsync(cancellationToken).ConfigureAwait(false);

    private Task<OperationResult> SubmitOperationAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return database.IsSiloLocalOperation(operation)
            ? Task.FromResult(database.ApplyLocalStorageOperation(database.PrepareStorageOperation(operation, cancellationToken), cancellationToken))
            : Owner.ApplyAsync(operation, cancellationToken);
    }
}
