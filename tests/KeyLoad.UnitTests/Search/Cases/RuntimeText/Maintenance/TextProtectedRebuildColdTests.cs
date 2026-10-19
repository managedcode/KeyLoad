namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextProtectedRebuildColdTests
{
    [Test]
    public Task ProtectedMutationAndFieldUseRevocationSurviveRemovedProjectionAndTwoSameOwnerColdOpens()
        => TextProtectedRebuildColdOwner.RunAsync(TextProtectedRebuildColdTrial.RunAsync,
            TestContext.Current!.Execution.CancellationToken);
}
