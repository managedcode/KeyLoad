using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.Core;

/// <summary>Coordinates direct embedded use of one real database engine.</summary>
/// <param name="database">Engine whose atomic store applies commands.</param>
public sealed class EmbeddedCoordinator(DatabaseEngine database) : ICommitCoordinator
{
    /// <inheritdoc />
    public Task<Result<TResult>> SubmitAsync<TResult>(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ApplyAsync<TResult>(database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<TResult>> RuntimeSubmitAsync<TResult>(OperationKind kind, Guid id, string principalId,
        object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var operation = sourcePlanningBudget is null
            ? database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), input) /* KLD0041: Command creation admits the exact Kind-selected input into the existing native persisted envelope without JSON conversion. */
            : (database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), input, sourcePlanningBudget) /* KLD0041: Command creation admits the exact Kind-selected input into the existing native persisted envelope without JSON conversion. */);
        return ApplyAsync<TResult>(operation, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<TResult>> SubmitVerifiedAsync<TResult>(Command<DatabaseCommand> operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return ApplyAsync<TResult>(database.OwnCommand(operation), cancellationToken);
    }

    /// <summary>Acknowledges the command after native process-persistent WAL publication.</summary>
    private async Task<Result<TResult>> ApplyAsync<TResult>(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        var /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ result = (await database.ApplyCoreAsync(operation, null, selectEmbeddedClock: true, cancellationToken)).Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
        return result.IsFailed ? Result<TResult>.Fail(result.Problem) : Result<TResult>.Succeed(result.Get<TResult>() /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */);
    }

    /// <inheritdoc />
    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
