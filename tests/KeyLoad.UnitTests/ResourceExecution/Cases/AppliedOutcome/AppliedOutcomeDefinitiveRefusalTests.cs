namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class AppliedOutcomeDefinitiveRefusalTests
{
    [Test]
    public Task ClaimedAckChangedAuthorizedNackReturnsDefinitiveConflictAndKeepsOriginalReceiptAcrossColdHealthyWork()
        => AppliedOutcomeDefinitiveRefusalFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
