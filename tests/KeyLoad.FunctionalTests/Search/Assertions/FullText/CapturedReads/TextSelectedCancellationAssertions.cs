using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextSelectedCancellationAssertions
{
    private const string Root = "root";
    private const long Revision = 1;
    private const double Score = 1d / 61d;

    internal static async Task VerifyAsync(TestDatabase database, TextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, CancellationToken token)
    {
        _ = await TextMaintenancePhaseFlow.FinishAsync(database, runtime, maintenance, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ServerFailureObserver.ObserveAsync(
                () => DuringAsync(database, runtime, maintenance, runtime.Search, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DuringAsync(TestDatabase database, TextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, SearchEngine search, CancellationToken token)
    {
        var selection = new TextIndexSelection(maintenance.Consumer, maintenance.IndexGeneration);
        var request = TextBilingualAudit.Request(database.Partition, "hello") with
        {
            TextIndex = selection
        };
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var originalReads = runtime.Owner.SelectedPostingReads(selection);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var observation = global::KeyLoad.Query.Search.TextPostingObservation.Enter(async _ => await original.CancelAsync());
        RankedDocument[]? partial = null;
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            partial = await search.SearchAsync(Root, request, original.Token)) ?? throw new InvalidOperationException();
        await Assert.That(error.CancellationToken).IsEqualTo(original.Token);
        observation.Dispose();
        await Assert.That(runtime.Owner.SelectedPostingReads(selection)).IsGreaterThan(originalReads);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = await search.SearchAsync(Root, request, token);
        var expected = new RankedDocument(new(database.ExpectedReference(new(database.Partition, TextBilingualAudit.Collection,
            TextBilingualAudit.EnglishId)), Revision, TextBilingualAudit.EnglishJson, false, []), Score);
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan().SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
