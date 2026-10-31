using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Submits authorized execution operations to Orleans; it owns no cluster lifecycle or protocol.</summary>
public sealed class SiloCommandCoordinator(DatabaseEngine database, IGrainFactory grains) : ICommitCoordinator
{
    private IDatabaseCommitGrain Owner => grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(database.Store.Identity.Incarnation));

    /// <inheritdoc />
    public Task<Result<object>> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitOperationAsync(database.CreateCommand(kind, id, principalId,
            database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<object>> RuntimeSubmitAsync(OperationKind kind, Guid id, string principalId,
        object input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var evaluatedAt = database.EvaluationClock.GetUtcNow();
        var operation = sourcePlanningBudget is null
            ? database.CreateCommand(kind, id, principalId, evaluatedAt, input)
            : database.CreateCommand(kind, id, principalId, evaluatedAt, input, sourcePlanningBudget);
        return SubmitOperationAsync(operation, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<object>> SubmitVerifiedAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return SubmitOperationAsync(database.OwnCommand(operation), cancellationToken);
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

    private Task<Result<object>> SubmitOperationAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return database.IsSiloLocalOperation(operation)
            ? Task.FromResult(database.ApplyLocalStorageOperation(database.PrepareStorageOperation(operation, cancellationToken), cancellationToken))
            : Owner.ApplyAsync(operation, cancellationToken);
    }
}
