using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextSelectedCancellationAssertions
{
    private const string Root = "root";
    private const long Revision = 1;
    private const double Score = 1d / 61d;

    internal static async Task VerifyAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, QueryObservedWorkClock clock, CancellationToken token)
    {
        _ = await NativeTextMaintenancePhaseFlow.FinishAsync(database, runtime, maintenance, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new NativeTextSelectedProjection(NativeTextBilingualAudit.Open(database), runtime.Owner, runtime.Owner);
            await ServerFailureObserver.ObserveAsync(
                () => DuringAsync(database, runtime, maintenance, new(database.Database,
                    UnitExecutionOptions.QueryExecution(), projection), clock, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DuringAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, SearchEngine search, QueryObservedWorkClock clock,
        CancellationToken token)
    {
        var selection = new TextIndexSelectionV1(maintenance.Consumer, maintenance.IndexGeneration);
        var request = NativeTextBilingualAudit.Request(database.Partition, "hello") with { TextIndex = selection };
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var originalReads = runtime.Owner.SelectedPostingReads(selection);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token);
        clock.Arm(() => runtime.Owner.SelectedPostingReads(selection) > originalReads, original.Cancel);
        RankedDocument[]? partial = null;
        try
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                partial = await search.SearchAsync(Root, request, original.Token)) ?? throw new InvalidOperationException();
            await Assert.That(error.CancellationToken).IsEqualTo(original.Token);
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(runtime.Owner.SelectedPostingReads(selection)).IsGreaterThan(originalReads);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = await search.SearchAsync(Root, request, token);
        var expected = new RankedDocument(new(new(database.Partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.EnglishId), Revision, NativeTextBilingualAudit.EnglishJson, false, []), Score);
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan().SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
