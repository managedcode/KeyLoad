using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed record ConnectionOperationGate(GrainRequestPhase Phase, bool Fail)
{
    internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
