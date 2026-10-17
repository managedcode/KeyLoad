namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Supporting six native production owners, separately qualified from mandatory Docker Aspire RF3.</summary>
[NotInParallel]
internal sealed class PartitionMovementLateNativeOwnerTests
{
    [Test]
    public Task SameFactorySealedExpiredRetireColdRecordsMissingAuthorityThenSameMoveAndOriginalReceiptAreHealthy()
        => PartitionMovementLateNativeTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
