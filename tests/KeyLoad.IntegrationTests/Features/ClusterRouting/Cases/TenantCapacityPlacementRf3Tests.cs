
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TenantCapacityPlacementRf3Tests
{
    [Test]
    public async Task OriginalPendingIntentSurvivesAdminRevocationButEpochRepairRequiresFreshIntentAndTwoColdReplays()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3RevocationScenario.RunAsync(linked.Token).ConfigureAwait(false);
    }

    [Test]
    public async Task OriginalCapacityHysteresisKeepsDedicatedOwnerThenEqualityDemotesAndReplaysAcrossTwoColdCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3DemotionScenario.RunAsync(linked.Token).ConfigureAwait(false);
    }

    [Test]
    public async Task OriginalExpiredSignedPlanRefusesWithoutClaimingGuidThenFreshPlanMovesAndReplaysAcrossColdCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3ExpiryScenario.RunAsync(linked.Token).ConfigureAwait(false);
    }

    [Test]
    public async Task SameTenantTwoMovesRetainRowCasAndOriginalReceiptsAcrossPartialPublicationAndTwoColdCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3MultiScenario.RunAsync(linked.Token).ConfigureAwait(false);
    }

    [Test]
    public async Task OriginalSignedCapacityPlanPersistsAppliesReplaysAndSurvivesTwoColdOwnerCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3Scenario.RunAsync(linked.Token).ConfigureAwait(false);
    }

    [Test]
    public async Task OriginalCapacityApplyResumesSameGuidAfterObservedCommittedPrepareOwnerLossAndTwoColdCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await TenantCapacityPlacementRf3Scenario.RunAsync(committedOwnerLoss: true, linked.Token).ConfigureAwait(false);
    }
}
