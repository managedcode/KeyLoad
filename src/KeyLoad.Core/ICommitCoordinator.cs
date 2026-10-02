namespace KeyLoad.Core;

/// <summary>Coordinates replicated command acceptance and linearizable read barriers.</summary>
public interface ICommitCoordinator
{
    /// <summary>Submits one stable command identity and verified principal to the owning coordinator.</summary>
    /// <returns>The persisted command result or an explicit acceptance failure.</returns>
    Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson, CancellationToken cancellationToken = default);
    /// <summary>Establishes the coordinator's committed read cut.</summary>
    /// <returns>A task completing when the read barrier is established.</returns>
    Task ReadBarrierAsync(CancellationToken cancellationToken = default);
}
