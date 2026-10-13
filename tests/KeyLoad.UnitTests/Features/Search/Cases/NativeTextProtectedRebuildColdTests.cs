namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProtectedRebuildColdTests
{
    [Test]
    public Task ProtectedMutationAndFieldUseRevocationSurviveRemovedProjectionAndTwoSameOwnerColdOpens()
        => NativeTextProtectedRebuildColdOwner.RunAsync(NativeTextProtectedRebuildColdTrial.RunAsync,
            TestContext.Current!.Execution.CancellationToken);
}
