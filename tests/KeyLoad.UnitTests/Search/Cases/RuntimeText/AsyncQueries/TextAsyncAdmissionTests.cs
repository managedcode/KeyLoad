using KeyLoad.Query;
using KeyLoad.Server.Search;
using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextAsyncAdmissionTests
{
    private const string Collection = "execution-text-async-admission";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string DocumentId = "one";
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AcFts007PreCancelledAsyncSearchCreatesNoReservationOrGeneration()
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
    public async Task AcFts007PostingPauseKeepsItsReservationWithoutRefusingOthersUntilCleanupJoins()
    {
        using var database = CreateDatabase();
        var postingObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePosting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseOnce = 1;
        using var projection = CreateProjection(database, stage =>
        {
            if (stage == TextFaultStage.PostingWritten && Interlocked.Exchange(ref pauseOnce, 0) == 1)
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
            active = Task.Run(() => search.SearchAsync("root", Request(database), cancellation.Token), CancellationToken.None);
            await postingObserved.Task.WaitAsync(CoordinationTimeout, TimeProvider.System);
            await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(1);
            await AssertConcurrentReservationAdmittedAsync(database);
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

    private static async Task AssertConcurrentReservationAdmittedAsync(TestDatabase database)
    {
        using (database.Database.AdmitQuery(TestToken()))
        {
            await Assert.That(database.Database.QueryReadsInFlight).IsEqualTo(2);
        }
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

    private static TestDatabase CreateDatabase()
    {
        var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, DocumentId, "{\"text\":\"needle\"}"));
        return database;
    }

    private static RuntimeTextProjection CreateProjection(TestDatabase database,
        Action<TextFaultStage>? faultObserver = null)
        => new(Path.Combine(database.Directory, "execution-text"),
            database.Store.Identity.NodeId, UnitTextOptions.Execution(), faultObserver: faultObserver);

    private static SearchRequest Request(TestDatabase database) => new(database.Partition, Collection, TextPath, Query);

    private static CancellationToken TestToken() => TestContext.Current!.Execution.CancellationToken;

    private static int GenerationCount(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "execution-text"))
            .Count(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
}
