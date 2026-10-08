using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextLaterStageCancellationAssertions
{
    internal static async Task VerifyAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityResult begin,
        QueryObservedWorkClock clock, CancellationToken token)
    {
        var page = database.Database.ReadProjectionBatch(NativeTextMaintenanceTestValues.Principal,
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
