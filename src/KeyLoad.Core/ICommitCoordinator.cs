namespace KeyLoad.Core;

/// <summary>Coordinates replicated command acceptance and linearizable read barriers.</summary>
public interface ICommitCoordinator
{
    /// <summary>Submits one stable command identity and verified principal to the owning coordinator.</summary>
    /// <returns>The persisted command result or an explicit acceptance failure.</returns>
    Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson, CancellationToken cancellationToken = default);
    /// <summary>Submits an attributed native command with an optional original call-local planning budget.</summary>
    /// <returns>The persisted typed command result or an explicit acceptance failure.</returns>
    Task<OperationResult> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget? sourcePlanningBudget = null,
        CancellationToken cancellationToken = default);
    /// <summary>Submits an already Core-issued operation after verifying its native authority.</summary>
    /// <param name="operation">Exact issued operation; this path cannot mint authority from public input.</param>
    /// <param name="cancellationToken">Original caller cancellation for admission and settlement.</param>
    /// <param name="sourcePlanningBudget">Original call-local planning budget, when supplied by its owner.</param>
    /// <returns>The original ordered coordinator result or explicit acceptance uncertainty.</returns>
    Task<OperationResult> SubmitVerifiedAsync(ReplicatedOperation operation, CancellationToken cancellationToken,
        ReadExecutionBudget? sourcePlanningBudget = null);
    /// <summary>Establishes the coordinator's committed read cut.</summary>
    /// <returns>A task completing when the read barrier is established.</returns>
    Task ReadBarrierAsync(CancellationToken cancellationToken = default);
}
