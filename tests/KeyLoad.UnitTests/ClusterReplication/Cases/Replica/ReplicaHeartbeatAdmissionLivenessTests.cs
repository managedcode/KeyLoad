namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Real three-silo native journal regression, separate from Aspire/public RF3 qualification.</summary>
internal sealed class ReplicaHeartbeatAdmissionLivenessTests
{
    [Test]
    public Task HeldOriginalAdmissionKeepsNativeHeartbeatsThenCommitsReplaysAndAdvances()
        => ReplicaHeartbeatAdmissionLivenessFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
