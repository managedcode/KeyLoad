namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class RuntimeJournalBootstrapAdmissionTests
{
    [Test]
    public Task ExactBootstrapUsesReservedLaneWhileDataAndOrdinaryJournalEffectsSettle()
        => RuntimeJournalBootstrapAdmissionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
