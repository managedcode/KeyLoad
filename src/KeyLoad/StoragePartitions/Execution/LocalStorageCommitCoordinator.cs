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

    public Task<Result<TResult>> SubmitAsync<TResult>(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitVerifiedAsync<TResult>(database.CreateCommand(kind, id, principalId,
            database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken);
    }

    public Task<Result<TResult>> RuntimeSubmitAsync<TResult>(OperationKind kind, Guid id, string principalId,
        object /* KLD0041: Implement the coordinator's current native command-input contract; the concrete input erases only into the existing generated DatabaseCommand.Input field. */ input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var now = database.EvaluationClock.GetUtcNow();
        var command = sourcePlanningBudget is null
            ? (database.CreateCommand(kind, id, principalId, now, input) /* KLD0041: Create the current generated DatabaseCommand.Input from the validated runtime command input; keep the actual asynchronous reply typed as TResult. */)
            : (database.CreateCommand(kind, id, principalId, now, input, sourcePlanningBudget) /* KLD0041: Create the current generated DatabaseCommand.Input from the validated runtime command input; keep the actual asynchronous reply typed as TResult. */);
        return SubmitVerifiedAsync<TResult>(command, cancellationToken, sourcePlanningBudget);
    }

    public Task<Result<TResult>> SubmitVerifiedAsync<TResult>(Command<DatabaseCommand> operation,
        CancellationToken cancellationToken, ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return ApplyAsync<TResult>(database.OwnCommand(operation), cancellationToken);
    }

    private async Task<Result<TResult>> ApplyAsync<TResult>(Command<DatabaseCommand> owned, CancellationToken cancellationToken)
    {
        if (database.IsSiloLocalOperation(owned))
        {
            var /* KLD0041: Hold the existing persisted StorageOperationResult.Result only until the concrete TResult is extracted at this local apply boundary. */ local = ((await database.ApplyCoreAsync(database.PrepareStorageOperation(owned, cancellationToken), null,
                selectEmbeddedClock: false, cancellationToken).ConfigureAwait(true)).Result /* KLD0041: Read the current persisted StorageOperationResult.Result field without changing its native mixed-outcome binary shape; immediately complete the typed caller. */);
            return local.IsFailed ? Result<TResult>.Fail(local.Problem) : Result<TResult>.Succeed((local.Get<TResult>() /* KLD0041: Extract the concrete TResult from the existing stored mixed outcome only at completion; the returned task remains Task<Result<TResult>>. */));
        }
        var retained = database.ReadStoredCommand(owned, cancellationToken).Value;
        var prepared = retained?.Command ?? database.PrepareStorageOperation(owned, cancellationToken);
        if (retained is null && database.LastApplied == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SequenceExhausted);
        }
        var sequence = retained?.Sequence ?? checked(database.LastApplied + NextSequence);
        var /* KLD0041: Hold the existing persisted StorageOperationResult.Result only until the concrete TResult is extracted at this local apply boundary. */ result = ((await database.ApplyStorageOperationsAsync([prepared], sequence, cancellationToken).ConfigureAwait(true)).Applied[0].Result /* KLD0041: Read the current persisted StorageOperationResult.Result field without changing its native mixed-outcome binary shape; immediately complete the typed caller. */);
        var /* KLD0041: Hold the existing persisted StorageOperationResult.Result only until the concrete TResult is extracted at this local apply boundary. */ acknowledged = (StorageCopyAcknowledgements.Attach(result, SharedFileCopies) /* KLD0041: Attach the actual shared-file copy acknowledgement to the current persisted mixed outcome before extracting the caller's concrete TResult. */);
        return acknowledged.IsFailed ? Result<TResult>.Fail(acknowledged.Problem)
            : Result<TResult>.Succeed((acknowledged.Get<TResult>() /* KLD0041: Extract the concrete TResult from the existing stored mixed outcome only at completion; the returned task remains Task<Result<TResult>>. */));
    }

    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = database.LastApplied;
        return Task.CompletedTask;
    }
}
