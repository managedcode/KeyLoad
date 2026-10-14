namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueDispatchCasWholeFlowTests
{
    [Test]
    public async Task VersionFencedQueueDispatchPreservesPoliciesDataFailuresAndOldReceiptsAcrossTwoReopensThenHealthyWork()
    {
        using var fixture = new QueueDispatchCasFixture();
        await QueueDispatchCasFlow.RunAsync(fixture);
    }

    [Test]
    public async Task ExhaustedQueueSchemaRefusesRealPauseAndRetainsCompleteMetadataBeforeHealthyNewOwner()
    {
        using (var maximum = new QueueDispatchCasFixture(long.MaxValue))
        {
            var command = maximum.Command(Guid.NewGuid(), long.MaxValue, true);
            await QueueDispatchCasAssertions.FailureAsync(maximum, maximum.Apply(command),
                ErrorCode.ResourceExhausted, false, long.MaxValue);
            maximum.Reopen();
            await QueueDispatchCasAssertions.FailureAsync(maximum, maximum.Apply(command),
                ErrorCode.ResourceExhausted, false, long.MaxValue);
        }
        using var healthy = new QueueDispatchCasFixture();
        await QueueDispatchCasFlow.RunAsync(healthy);
    }
}
