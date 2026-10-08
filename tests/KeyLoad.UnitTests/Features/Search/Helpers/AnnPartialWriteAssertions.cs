using KeyLoad.Orleans;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnPartialWriteAssertions
{
    private const int MaximumPages = 12;
    private const int First = 0;
    private const long Empty = 0;

    internal static async Task PrepareAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request)
    {
        var began = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Begin);
        var through = began.Source!.ThroughSequence;
        var completed = false;
        for (var number = First; number < MaximumPages; number++)
        {
            var page = NativeAnnMaintenanceTestData.Read(database, request, through);
            if (page.Entries.IsEmpty)
            { throw new InvalidOperationException("The actual partial-write fixture requires a nonempty durable checkpoint."); }
            var intent = NativeAnnMaintenanceTestData.Intent(page);
            _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
            if (!page.HasMore)
            { completed = true; break; }
        }
        if (!completed)
        { throw new InvalidOperationException("The actual partial-write fixture exceeds its original page bound."); }
        _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Verify);
    }

    internal static async Task CancelAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request, AnnPublicObservedWorkClock clock)
    {
        var image = NativeAnnMaintenanceTestData.Snapshot(database);
        var cut = database.Store.Position;
        var files = NativeAnnFixtureFileSnapshot.Capture(database.Directory);
        using var child = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        clock.Arm(() => runtime.Owner.ObserveWork(runtime.SessionId) is { } observed
            && observed.SessionId == runtime.SessionId && observed.Stage == AnnMaintenanceCapabilityKind.Publish
            && observed.WrittenBytes > Empty, child.Cancel);
        AnnMaintenanceCapabilityResult? partial = null;
        try
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => partial =
                await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Publish, token: child.Token))
                ?? throw new InvalidOperationException("The actual native frame writer did not observe current-child cancellation.");
            await Assert.That(error.CancellationToken).IsEqualTo(child.Token);
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        var actual = runtime.Owner.ObserveWork(runtime.SessionId)
            ?? throw new InvalidOperationException("The actual canceled native writer observation is missing.");
        await Assert.That(actual.Stage).IsEqualTo(AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(actual.WrittenBytes).IsGreaterThan(Empty);
        await Assert.That(partial).IsNull();
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
        var healthy = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(healthy.Pending).IsFalse();
        await Assert.That(healthy.Count).IsEqualTo(AnnPublicWholeFlow.Count);
        await Assert.That(healthy.IndexSha256).IsNotNull();
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        var engine = AnnPublicWholeFlow.Engine(database, runtime);
        await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal,
            AnnPublicWholeFlow.Request(database, request)), database, cut, AnnPageMode.Exact);
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
    }
}
