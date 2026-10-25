using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextLaterStageCancellationAssertions
{
    internal static async Task VerifyAsync(TestDatabase database, TextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityResult begin,
        QueryObservedWorkClock clock, CancellationToken token)
    {
        var page = database.Database.ReadProjectionBatch(TextMaintenanceTestValues.Principal,
            new(request.Consumer, ThroughSequence: begin.ReplayUpperSequence));
        var original = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        using var stage = CancellationTokenSource.CreateLinkedTokenSource(token);
        clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes,
            stage.Cancel);
        TextMaintenanceCapabilityResult? partial = null;
        try
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            {
                partial = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.PreparePage,
                    page, original, token: stage.Token);
            }) ?? throw new InvalidOperationException();
            await Assert.That(error.CancellationToken).IsEqualTo(stage.Token);
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before.RangeExaminedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
