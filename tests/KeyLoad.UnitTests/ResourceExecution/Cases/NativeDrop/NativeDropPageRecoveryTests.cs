namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class NativeDropPageRecoveryTests
{
    [Test]
    public Task GenuineFailedPageClosesThenRetriesHealthyAcrossTwoJoinedColdReopens()
        => NativeDropPageRecoveryFlow.RunAsync(NativeDropPageFailureRecovery.ExecuteAsync, unknown: false);

    [Test]
    public Task GenuinePersistedUnknownCannotCloseOrAdvanceAndSurvivesTwoJoinedColdReopens()
        => NativeDropPageRecoveryFlow.RunAsync(NativeDropPageUnknownRecovery.ExecuteAsync, unknown: true);

    [Test]
    public Task GenuineNeverIssuedCloseRefusesHeldLatePersistAndOriginalPageThenRetriesHealthy()
        => NativeDropPageRecoveryFlow.RunAsync(NativeDropPageNeverIssuedRecovery.ExecuteAsync, unknown: false);
}
