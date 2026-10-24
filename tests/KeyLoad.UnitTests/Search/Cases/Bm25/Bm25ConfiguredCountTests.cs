namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25ConfiguredCountTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public Task OriginalIssuedPublicationHonorsLowerRetainedCountAcrossSameOwnerColdReopen(int maximumHeads)
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(flow => Bm25ConfiguredCountTrial.RunAsync(flow, maximumHeads));
}
