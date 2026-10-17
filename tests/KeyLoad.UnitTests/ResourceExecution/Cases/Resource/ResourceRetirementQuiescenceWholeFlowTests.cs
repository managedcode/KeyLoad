using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Real two-owner quiescence and recovery; authentic all-voter Begin remains a separate RF3 gate.</summary>
internal sealed class ResourceRetirementQuiescenceWholeFlowTests
{
    [Test]
    public Task PreparingReservationsRejectNewMovementWhileAcceptedAbortAndBusinessResumeAcrossTwoColdReopens()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(ResourceRetirementQuiescenceFlow.ExecuteAsync);
}
