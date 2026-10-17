namespace KeyLoad.IntegrationTests.Features.Messaging;

[NotInParallel]
internal sealed class DueNoQuorumRf3Tests
{
    [Test]
    public Task AcDue003NoQuorumDefersRecurringOccurrenceUntilTwoVotersReturn()
        => DueNoQuorumRf3Run.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
}
