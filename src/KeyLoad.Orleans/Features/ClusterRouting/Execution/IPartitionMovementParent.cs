using KeyLoad.Core;

namespace KeyLoad.Orleans;

/// <summary>Borrows node-local movement orchestration behind one signed independently keyed public request.</summary>
internal interface IPartitionMovementParent
{
    /// <summary>Executes bounded current-state transfer/reconciliation with individually authorized native child phases.</summary>
    /// <param name="trusted">Verified original public identity, never a caller-supplied role or grant.</param>
    /// <param name="request">Existing frozen typed movement request.</param>
    /// <param name="work">Already owned request read work and original deadline.</param>
    /// <param name="progress">Awaited native CQRS writer for actual settled durable phases.</param>
    /// <param name="phaseObservation">Optional identity-only native fixture observation, never effect authority.</param>
    /// <param name="cancellationToken">Original public request cancellation.</param>
    /// <returns>Only the actual settled durable movement result.</returns>
    Task<PartitionMoveResult> ExecuteAsync(GrainRequestEnvelope trusted, PartitionMoveRequest request,
        ReadExecutionBudget work, Func<PartitionMovePhase, ValueTask> progress,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken);
}
