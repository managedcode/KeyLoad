namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class RuntimeJournalBootstrapAdmissionTests
{
    [Test]
    public Task ExactBootstrapUsesReservedLaneWhileDataAndOrdinaryJournalEffectsSettle()
        => RuntimeJournalBootstrapAdmissionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
