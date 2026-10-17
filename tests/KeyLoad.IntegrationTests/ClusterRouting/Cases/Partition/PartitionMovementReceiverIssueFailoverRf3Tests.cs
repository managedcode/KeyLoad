namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual receiver first issuance survives read failover without replacing its original effect.</summary>
[NotInParallel]
internal sealed class PartitionMovementReceiverIssueFailoverRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualIssueAckReadFailoverRetainsOriginalAuthorityAndColdSdkMcpQ1HealthyContinuation(bool allReceivers)
        => PartitionMovementReceiverIssueFailoverRf3Trial.RunAsync(allReceivers,
            TestContext.Current!.Execution.CancellationToken);
}
