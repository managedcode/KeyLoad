using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Server;

/// <summary>Applies shared-volume work inside the one ordinary Orleans owner's serialized invocation.</summary>
internal sealed class LocalStorageCommitCoordinator(DatabaseEngine database) : ICommitCoordinator
{
    private const long NextSequence = 1;
    private const int SharedFileCopies = 1;
    private const string SequenceExhausted = "The canonical storage sequence is exhausted.";

    public Task<Result<object>> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitVerifiedAsync(database.CreateCommand(kind, id, principalId,
            database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken);
    }

    public Task<Result<object>> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var now = database.EvaluationClock.GetUtcNow();
        var command = sourcePlanningBudget is null
            ? database.CreateCommand(kind, id, principalId, now, payload)
            : database.CreateCommand(kind, id, principalId, now, payload, sourcePlanningBudget);
        return SubmitVerifiedAsync(command, cancellationToken, sourcePlanningBudget);
    }

    public Task<Result<object>> SubmitVerifiedAsync(Command<DatabaseCommand> operation,
        CancellationToken cancellationToken, ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var owned = database.OwnCommand(operation);
        if (database.IsSiloLocalOperation(owned))
        {
            var local = database.PrepareStorageOperation(owned, cancellationToken);
            return Task.FromResult(database.ApplyLocalStorageOperation(local, cancellationToken));
        }
        var retained = database.ReadStoredCommand(owned, cancellationToken);
        var prepared = retained?.Command ?? database.PrepareStorageOperation(owned, cancellationToken);
        if (retained is null && database.LastApplied == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SequenceExhausted);
        }
        var sequence = retained?.Sequence ?? checked(database.LastApplied + NextSequence);
        var result = database.ApplyStorageOperation(prepared, sequence, cancellationToken).Result;
        return Task.FromResult(StorageCopyAcknowledgements.Attach(result, SharedFileCopies));
    }

    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = database.LastApplied;
        return Task.CompletedTask;
    }
}
