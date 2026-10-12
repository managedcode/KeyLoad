namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionMovementOutcomeWireTests
{
    [Test]
    public async Task OutcomePathReadsExactOriginalBytesAndWrongPurposeRejectsBeforeHealthyNextRequest()
    {
        await PartitionMovementNativeOutcomeCodecOwner.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
    }
}
