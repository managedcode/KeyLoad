
namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionBatchBudgetTests
{
    [Test]
    [Arguments(4)]
    [Arguments(6)]
    public async Task AcEventRetention002MultiplePurgeEffectsShareOneLedgerAndRollbackEverySource(int scanLimit)
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        fixture.SetLimits(new() { MaxScanRecords = scanLimit });
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new PurgeTopic("topic", 1), new PurgeTopic("topic", 2)]);
        await TopicRetentionAssertions.Reject(fixture, command, ErrorCode.ResourceExhausted);
        await Assert.That(fixture.Submit(command).SafeDetail).IsEqualTo("The topic purge scan budget is exhausted.");
        fixture.SetLimits(new());
        fixture.Submit(fixture.Purge()).Get<CommitReceipt>();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }
}
