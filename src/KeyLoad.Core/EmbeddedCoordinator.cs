using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.Core;

/// <summary>Coordinates direct embedded use of one real database engine.</summary>
/// <param name="database">Engine whose atomic store applies commands.</param>
public sealed class EmbeddedCoordinator(DatabaseEngine database) : ICommitCoordinator
{
    /// <inheritdoc />
    public Task<Result<object>> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ApplyAsync(database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<object>> RuntimeSubmitAsync(OperationKind kind, Guid id, string principalId,
        object input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var operation = sourcePlanningBudget is null
            ? database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), input)
            : database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), input, sourcePlanningBudget);
        return ApplyAsync(operation, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<object>> SubmitVerifiedAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        return ApplyAsync(database.OwnCommand(operation), cancellationToken);
    }

    /// <summary>Acknowledges the command after native process-persistent WAL publication.</summary>
    private async Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        var result = (await database.ApplyCoreAsync(operation, null, selectEmbeddedClock: true, cancellationToken)).Result;
        return result;
    }

    /// <inheritdoc />
    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
