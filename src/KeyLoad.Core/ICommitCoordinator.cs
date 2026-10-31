using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.Core;

/// <summary>Coordinates replicated command acceptance and linearizable read barriers.</summary>
public interface ICommitCoordinator
{
    /// <summary>Submits one stable command identity and verified principal to the owning coordinator.</summary>
    /// <returns>The persisted command result or an explicit acceptance failure.</returns>
    Task<Result<object>> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson, CancellationToken cancellationToken = default);
    /// <summary>Submits an attributed execution command with an optional original call-local planning budget.</summary>
    /// <returns>The persisted typed command result or an explicit acceptance failure.</returns>
    Task<Result<object>> RuntimeSubmitAsync(OperationKind kind, Guid id, string principalId,
        object input, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default);
    /// <summary>Submits a server-bound execution command with its original prepared database facts.</summary>
    /// <param name="operation">Owned execution command whose subject was verified by the server.</param>
    /// <param name="cancellationToken">Original caller cancellation for admission and settlement.</param>
    /// <param name="sourcePlanningBudget">Original call-local planning budget, when supplied by its owner.</param>
    /// <returns>The original ordered coordinator result or explicit acceptance uncertainty.</returns>
    Task<Result<object>> SubmitVerifiedAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null);
    /// <summary>Establishes the coordinator's committed read cut.</summary>
    /// <returns>A task completing when the read barrier is established.</returns>
    Task ReadBarrierAsync(CancellationToken cancellationToken = default);
}
