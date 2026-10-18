namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Native Orleans exclusion proof; successful concurrent same-file ownership remains unsupported.</summary>
[NotInParallel]
internal sealed class FunctionalFileOwnershipClusterTests
{
    [Test]
    public Task DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens()
        => FunctionalGateColdScenario.RunFileOwnershipAsync(TestContext.Current!.Execution.CancellationToken);
}
