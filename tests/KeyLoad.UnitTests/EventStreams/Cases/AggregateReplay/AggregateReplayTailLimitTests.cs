namespace KeyLoad.UnitTests.EventStreams;

internal sealed class AggregateReplayTailLimitTests
{
    [Test]
    public async Task AcEvent010CompleteTailAndSharedLimitsRejectWholeRequests()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(new EventData("replay-event-1", AggregateReplayFixture.EventType, """{"increment":1}"""),
            new EventData("replay-event-2", AggregateReplayFixture.EventType, """{"increment":2}"""),
            new EventData("replay-event-3", AggregateReplayFixture.EventType, """{"increment":3}"""));
        fixture.Reopen(new()
        {
            MaxResults = 2,
            MaxScanRecords = 2
        });

        var tooLarge = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 3));
        var incomplete = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 2));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(maximumEvents: 2,
            cancellationToken: cancellation.Token));

        await Assert.That(tooLarge.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(incomplete.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
