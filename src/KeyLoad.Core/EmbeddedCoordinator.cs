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
        return Task.FromResult(database.ApplyEmbedded(database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), payloadJson), cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<object>> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var operation = sourcePlanningBudget is null
            ? database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), payload)
            : database.CreateCommand(kind, id, principalId, database.EvaluationClock.GetUtcNow(), payload, sourcePlanningBudget);
        return Task.FromResult(database.ApplyEmbedded(operation, cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<object>> SubmitVerifiedAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePlanningBudget?.Check();
        var verified = database.OwnCommand(operation);
        return Task.FromResult(database.ApplyEmbedded(verified, cancellationToken));
    }

    /// <inheritdoc />
    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
