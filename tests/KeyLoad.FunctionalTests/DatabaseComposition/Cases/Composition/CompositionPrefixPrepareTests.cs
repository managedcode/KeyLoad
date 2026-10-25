namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed class CompositionPrefixPrepareTests
{
    [Test]
    public Task StagedPutAndOpaqueEnqueueFeedForwardAndTwoColdReplays()
        => CompositionPrefixPrepareFlow.RunAsync(CompositionPrefixPrepareData.Absent,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task NameOnlyDynamicEndpointsSeeStagedPutAndKeepOriginalLifetimesAcrossCold()
        => CompositionPrefixPrepareFlow.RunAsync(CompositionPrefixPrepareData.EffectCap,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CancelledStagedInvalidLinkIsNotParsedByLaterForward()
        => CompositionPrefixPrepareFlow.RunAsync(CompositionPrefixPrepareData.FirstRevision,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task StagedEdgeFeedsReverseThenForwardWithOriginalBoundEndpoints()
        => CompositionPrefixPrepareFlow.RunAsync(CompositionPrefixPrepareData.CancelRevision,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ScheduledAndExpiredInvalidLinksAreSkippedWithoutPayloadParsing(bool expired)
        => CompositionPrefixPrepareFlow.TemporalAsync(expired, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ThreeEffectCapAllowsFourTypedLifetimesAndLateConflictRollsBack()
        => CompositionPrefixPrepareFlow.CapacityAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CoarseAuthorizationWinsBeforeRealSourceLookaheadBudgetFailure()
        => CompositionPrefixPrepareRefusals.CoarseAuthorizationAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task CommittedClockPrecedesLaterPrefixValidationOrLookaheadFailure(bool budget)
        => CompositionPrefixPrepareClock.RunAsync(budget, TestContext.Current!.Execution.CancellationToken);

}
