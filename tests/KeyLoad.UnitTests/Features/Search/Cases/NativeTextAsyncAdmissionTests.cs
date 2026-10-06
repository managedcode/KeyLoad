using KeyLoad.Query;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextAsyncAdmissionTests
{
    private const string Collection = "native-text-async-admission";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string DocumentId = "one";
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AcFts007PreCancelledAsyncSearchCreatesNoReservationOrNativeGeneration()
    {
        using var database = CreateDatabase();
        using var projection = CreateProjection(database);
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            search.SearchAsync("root", Request(database), cancellation.Token));
        await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(GenerationCount(database)).IsEqualTo(0);

        var following = await search.SearchAsync("root", Request(database), TestToken());
        await Assert.That(following).HasSingleItem();
        await Assert.That(following[0].Document.Reference.Id).IsEqualTo(DocumentId);
    }

    [Test]
    public async Task AcFts007NativePostingPauseKeepsAdmissionUntilCancellationCleanupJoins()
    {
        using var database = CreateDatabase(new() { MaxConcurrentQueries = 1 });
        var postingObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePosting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseOnce = 1;
        using var projection = CreateProjection(database, stage =>
        {
            if (stage == NativeTextFaultStage.NativePostingWritten && Interlocked.Exchange(ref pauseOnce, 0) == 1)
            {
                postingObserved.TrySetResult();
                releasePosting.Task.GetAwaiter().GetResult();
            }
        });
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        using var cancellation = new CancellationTokenSource();
        Task<RankedDocument[]>? active = null;
        var lifetime = new AnalyticalAdmissionTaskLifetime(CoordinationTimeout);

        await lifetime.RunWithCleanupAsync(async () =>
        {
            active = search.SearchAsync("root", Request(database), cancellation.Token);
            await postingObserved.Task.WaitAsync(CoordinationTimeout, TimeProvider.System);
            await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(1);
            await AssertSaturatedSearchRejectedWithoutReadsAsync(database, search);
            await cancellation.CancelAsync();
            await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(1);
        }, cancellation.CancelAsync, () => ReleasePostingAsync(releasePosting),
            () => AssertCancelledWorkerJoinedAsync(active));

        await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(GenerationCount(database)).IsEqualTo(0);
        var healthy = await search.SearchAsync("root", Request(database), TestToken());
        await Assert.That(healthy).HasSingleItem();
        await Assert.That(healthy[0].Document.Reference.Id).IsEqualTo(DocumentId);
    }

    private static async Task AssertSaturatedSearchRejectedWithoutReadsAsync(TestDatabase database,
        SearchEngine search)
    {
        var before = database.Store.GetReadDiagnostics();
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            search.SearchAsync("root", Request(database), TestToken()));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(database.Store.GetReadDiagnostics()).IsEqualTo(before);
        await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(1);
    }

    private static async Task AssertCancelledWorkerJoinedAsync(Task<RankedDocument[]>? worker)
    {
        if (worker is null)
        {
            return;
        }
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => worker.WaitAsync(CoordinationTimeout, TimeProvider.System));
    }

    private static Task ReleasePostingAsync(TaskCompletionSource release)
    {
        release.TrySetResult();
        return Task.CompletedTask;
    }

    private static TestDatabase CreateDatabase(DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, DocumentId, "{\"text\":\"needle\"}"));
        return database;
    }

    private static NativeTextProjection CreateProjection(TestDatabase database,
        Action<NativeTextFaultStage>? faultObserver = null)
        => new(Path.Combine(database.Directory, "native-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution(), faultObserver: faultObserver);

    private static SearchRequest Request(TestDatabase database) => new(database.Partition, Collection, TextPath, Query);

    private static CancellationToken TestToken() => TestContext.Current!.Execution.CancellationToken;

    private static int GenerationCount(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "native-text"))
            .Count(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
}
