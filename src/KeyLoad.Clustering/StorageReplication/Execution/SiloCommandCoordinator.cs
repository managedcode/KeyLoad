using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Submits authorized execution operations to Orleans; it owns no cluster lifecycle or protocol.</summary>
public sealed class SiloCommandCoordinator(DatabaseEngine database, IGrainFactory grains) : ICommitCoordinator
{
    private IDatabaseCommitGrain Owner => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(database.Store.Identity.Incarnation));

    /// <inheritdoc />
    public Task<Result<TResult>> SubmitAsync<TResult>(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitOperationAsync<TResult>(database.CreateCommand(kind, id, principalId,
            database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<TResult>> RuntimeSubmitAsync<TResult>(OperationKind kind, Guid id, string principalId,
        object /* KLD0041: Use the current canonical DatabaseCommand input or persisted storage-result shape at this local storage boundary; RPC completion stays Result<TResult>. */ input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var evaluatedAt = database.EvaluationClock.GetUtcNow();
        var operation = sourcePlanningBudget is null
            ? (database.CreateCommand(kind, id, principalId, evaluatedAt, input) /* KLD0041: Use the current canonical DatabaseCommand input or persisted storage-result shape at this local storage boundary; RPC completion stays Result<TResult>. */)
            : (database.CreateCommand(kind, id, principalId, evaluatedAt, input, sourcePlanningBudget) /* KLD0041: Use the current canonical DatabaseCommand input or persisted storage-result shape at this local storage boundary; RPC completion stays Result<TResult>. */);
        return SubmitOperationAsync<TResult>(operation, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<TResult>> SubmitVerifiedAsync<TResult>(Command<DatabaseCommand> operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return SubmitOperationAsync<TResult>(database.OwnCommand(operation), cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        var cut = await Owner.ReadCutAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (database.Store.Identity.Incarnation != cut.Incarnation || database.LastApplied < cut.Position)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.RecoveryRequired);
        }
    }

    private Task<Result<TResult>> SubmitOperationAsync<TResult>(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return database.IsOwnedSiloLocalOperation(operation)
            ? ApplyLocalAsync<TResult>(operation, cancellationToken)
            : Owner.ApplyAsync<TResult>(operation, cancellationToken);
    }

    private async Task<Result<TResult>> ApplyLocalAsync<TResult>(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        var /* KLD0041: Use the current canonical DatabaseCommand input or persisted storage-result shape at this local storage boundary; RPC completion stays Result<TResult>. */ result = ((await database.ApplyCoreAsync(database.PrepareStorageOperation(operation, cancellationToken), null,
            selectEmbeddedClock: false, cancellationToken)).Result /* KLD0041: Use the current canonical DatabaseCommand input or persisted storage-result shape at this local storage boundary; RPC completion stays Result<TResult>. */);
        return result.IsFailed ? Result<TResult>.Fail(result.Problem) : Result<TResult>.Succeed((result.Get<TResult>() /* KLD0041: Use the current canonical DatabaseCommand input or persisted storage-result shape at this local storage boundary; RPC completion stays Result<TResult>. */));
    }
}
