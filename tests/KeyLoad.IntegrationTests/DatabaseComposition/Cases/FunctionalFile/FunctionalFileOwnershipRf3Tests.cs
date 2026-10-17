namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Native RF3 exclusion proof; successful concurrent same-file ownership remains unsupported.</summary>
[NotInParallel]
internal sealed class FunctionalFileOwnershipRf3Tests
{
    [Test]
    public Task DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens()
        => FunctionalGateColdScenario.RunFileOwnershipAsync(TestContext.Current!.Execution.CancellationToken);
}
