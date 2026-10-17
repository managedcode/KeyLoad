namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class MetamorphicPublicRf3Tests
{
    [Test]
    [Arguments(1, false, 0)]
    [Arguments(1, true, 2)]
    [Arguments(17, false, 2)]
    [Arguments(17, true, 0)]
    public Task GeneratedSupportedSqlJsonAndCSharpMatchIndependentReferenceThroughLivePolicyRepairReplayAndTwoColdCuts(
        int seed, bool descending, int offset) => MetamorphicPublicFlow.RunAsync(seed, descending, offset);
}
