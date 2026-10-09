namespace KeyLoad.UnitTests.Features.Search;

/// <summary>AC-SEARCH-WAIT-001: real minimum/applied authority rejection precedes complete literal native continuation.</summary>
internal sealed class SearchWaitAuthorityTests
{
    [Test]
    public Task WrongAtomicPartitionCannotPublishIndexAndOriginalAuthorityReturnsLiteralHealthy()
        => SearchWaitAuthorityTrial.TokenAsync(static token => token with
        { AtomicPartitionId = SearchWaitAuthorityState.ForeignAtomicPartition }, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ChangedPlacementEpochCannotPublishIndexAndOriginalAuthorityReturnsLiteralHealthy()
        => SearchWaitAuthorityTrial.TokenAsync(static token => token with
        { OwnershipEpoch = checked(token.OwnershipEpoch + SearchWaitAuthorityState.DifferentEpoch) }, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ZeroMinimumPositionCannotPublishIndexAndOriginalAuthorityReturnsLiteralHealthy()
        => SearchWaitAuthorityTrial.TokenAsync(static token => token with
        { Position = SearchWaitAuthorityState.InvalidPosition }, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task MissingOrNegativeAppliedAuthorityReturnsNoPartialAndExactRepairPermitsLiteralHealthy(bool missing)
        => SearchWaitAuthorityTrial.AppliedAsync(missing, TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task AbsentNativeProviderReturnsNoPartialThenActualProviderReturnsLiteralHealthy()
        => SearchWaitAuthorityTrial.ProviderAsync(TestContext.Current!.Execution.CancellationToken);
}
