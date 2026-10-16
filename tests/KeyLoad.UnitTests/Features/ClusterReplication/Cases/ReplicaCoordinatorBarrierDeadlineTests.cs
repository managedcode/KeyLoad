namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Real three-silo barrier RPCs retain the enclosing deadline while native leader admission is owned.</summary>
internal sealed class ReplicaCoordinatorBarrierDeadlineTests
{
    /// <summary>Both native read purposes survive owned admission while cancellation and genuine noquorum remain bounded.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForwardedBarrierSurvivesShortRpcDeadlineAndRetainsCancellationAndNoQuorum(bool control)
        => await ReplicaBarrierDeadlineFlow.RunAsync(control, TestContext.Current!.Execution.CancellationToken);
}
