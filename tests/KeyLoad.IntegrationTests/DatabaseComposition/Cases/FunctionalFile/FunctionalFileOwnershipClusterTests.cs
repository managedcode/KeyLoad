namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Native CLUSTER exclusion proof; successful concurrent same-file ownership remains unsupported.</summary>
[NotInParallel]
internal sealed class FunctionalFileOwnershipClusterTests
{
    [Test]
    public Task DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens()
        => FunctionalGateColdScenario.RunFileOwnershipAsync(TestContext.Current!.Execution.CancellationToken);
}
