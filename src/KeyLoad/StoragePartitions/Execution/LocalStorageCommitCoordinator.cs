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

    public Task<Result<object>> RuntimeSubmitAsync(OperationKind kind, Guid id, string principalId,
        object input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var now = database.EvaluationClock.GetUtcNow();
        var command = sourcePlanningBudget is null
            ? database.CreateCommand(kind, id, principalId, now, input)
            : database.CreateCommand(kind, id, principalId, now, input, sourcePlanningBudget);
        return SubmitVerifiedAsync(command, cancellationToken, sourcePlanningBudget);
    }

    public async Task<Result<object>> SubmitVerifiedAsync(Command<DatabaseCommand> operation,
        CancellationToken cancellationToken, ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var result = await ApplyAsync(database.OwnCommand(operation), cancellationToken).ConfigureAwait(true);
        // The shared file copy acknowledges only what ZoneTree's write-ahead log holds durably.
        await database.Store.SyncAsync(CancellationToken.None).ConfigureAwait(true);
        return result;
    }

    private async Task<Result<object>> ApplyAsync(Command<DatabaseCommand> owned, CancellationToken cancellationToken)
    {
        if (database.IsSiloLocalOperation(owned))
        {
            return (await database.ApplyCoreAsync(database.PrepareStorageOperation(owned, cancellationToken), null,
                selectEmbeddedClock: false, cancellationToken).ConfigureAwait(true)).Result;
        }
        var retained = database.ReadStoredCommand(owned, cancellationToken).Value;
        var prepared = retained?.Command ?? database.PrepareStorageOperation(owned, cancellationToken);
        if (retained is null && database.LastApplied == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SequenceExhausted);
        }
        var sequence = retained?.Sequence ?? checked(database.LastApplied + NextSequence);
        var result = (await database.ApplyStorageOperationsAsync([prepared], sequence, cancellationToken).ConfigureAwait(true)).Applied[0].Result;
        return StorageCopyAcknowledgements.Attach(result, SharedFileCopies);
    }

    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = database.LastApplied;
        return Task.CompletedTask;
    }
}
