namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobPriorPublicationProcessRecoveryTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PriorAcknowledgedPublicationSurvivesIndependentQueueCutThenLifetimeRepairAndReuse(bool missing)
        => await BlobPriorPublicationProcess.RunAsync(missing, TestContext.Current!.Execution.CancellationToken);
}
