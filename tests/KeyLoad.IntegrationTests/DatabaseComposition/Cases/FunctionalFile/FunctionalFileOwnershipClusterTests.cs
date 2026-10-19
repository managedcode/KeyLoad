namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Execution Orleans exclusion proof; successful concurrent same-file ownership remains unsupported.</summary>
[NotInParallel]
internal sealed class FunctionalFileOwnershipClusterTests
{
    [Test]
    public Task DistinctFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens()
        => FunctionalGateColdScenario.RunFileOwnershipAsync(TestContext.Current!.Execution.CancellationToken);
}
