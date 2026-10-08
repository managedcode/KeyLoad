using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextSelectedShutdownAssertions
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
            await ServerFailureObserver.ObserveAsync(() => HeldAsync(database, runtime, maintenance,
                new(database.Database, UnitExecutionOptions.QueryExecution(), projection), clock, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var reopened = new NativeTextMaintenanceTestRuntime(database);
            using var projection = new NativeTextSelectedProjection(NativeTextBilingualAudit.Open(database), reopened.Owner, reopened.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                await LiteralAsync(database, await search.SearchAsync(Root, Request(database, maintenance), token));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HeldAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest maintenance, SearchEngine search, QueryObservedWorkClock clock, CancellationToken token)
    {
        var request = Request(database, maintenance);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var reads = runtime.Owner.SelectedPostingReads(request.TextIndex!);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        clock.Arm(() => runtime.Owner.SelectedPostingReads(request.TextIndex!) > reads,
            () => { entered.TrySetResult(); release.Wait(token); });
        Task<RankedDocument[]>? original = null;
        Task? shutdown = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                original = search.SearchAsync(Root, request, token);
                await entered.Task.WaitAsync(token);
                shutdown = runtime.Owner.DisposeAsync().AsTask();
                await Assert.That(shutdown.IsCompleted).IsFalse();
                await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => search.SearchAsync(Root, request, token));
            }, failures);
        }
        finally
        {
            release.Set();
            clock.Disarm();
            if (original is not null)
            { await ServerFailureObserver.ObserveAsync(async () => await LiteralAsync(database, await original), failures); }
            if (shutdown is not null)
            { await ServerFailureObserver.ObserveAsync(() => shutdown, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    private static SearchRequest Request(TestDatabase database, TextIndexMaintenanceRequest maintenance)
        => NativeTextBilingualAudit.Request(database.Partition, NativeTextBilingualAudit.EnglishQuery)
            with
        { TextIndex = new(maintenance.Consumer, maintenance.IndexGeneration) };

    private static async Task LiteralAsync(TestDatabase database, RankedDocument[] actual)
    {
        var expected = new RankedDocument(new(new(database.Partition, NativeTextBilingualAudit.Collection,
            NativeTextBilingualAudit.EnglishId), Revision, NativeTextBilingualAudit.EnglishJson, false, []), Score);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
    }
}
