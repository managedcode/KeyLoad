using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextGenerationCensusOverlap
{
    private const string Collection = "native-text-census-overlap";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const int WaitSeconds = 30;

    internal static async Task RunAsync(CancellationToken cancellation)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        using var pause = new NativeTextGenerationPause();
        var root = Path.Combine(database.Directory, Collection);
        using var projection = new NativeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution(), faultObserver: pause.Observe);
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var original = await engine.SearchAsync("root", request, cancellation);
        using var lease = projection.Acquire(CaptureScope(database), new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)));
        lease.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
        lease.ObserveToken(Query);
        await RunReplacementAsync(database, engine, request, root, original, lease, pause,
            cancellation);
    }

    private static async Task RunReplacementAsync(TestDatabase database,
        SearchEngine engine, SearchRequest request, string root, RankedDocument[] original,
        ITextProjectionLease lease, NativeTextGenerationPause pause, CancellationToken cancellation)
    {
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
        pause.Arm();
        using var replacementCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var replacementTask = Task.Run(() => engine.Search("root", request, replacementCancellation.Token));
        var joined = false;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                pause.WaitUntilEntered(cancellation);
                lease.Dispose();
                pause.Resume();
                var replacement = await replacementTask.WaitAsync(TimeSpan.FromSeconds(WaitSeconds), TimeProvider.System, cancellation);
                joined = true;
                await Assert.That(replacement).HasSingleItem();
                await Assert.That(replacement[0].Document.Revision).IsGreaterThan(original[0].Document.Revision);
                await Assert.That(GenerationPaths(root)).HasSingleItem();
            }
            finally
            {
                pause.Resume();
                if (!joined)
                {
                    await ServerFailureObserver.ObserveAsync(replacementCancellation.CancelAsync, failures);
                    await ServerFailureObserver.ObserveAsync(async () => await replacementTask, failures);
                }
                ServerFailureObserver.Observe(lease.Dispose, failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, TextPath, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }

    private static string[] GenerationPaths(string root)
        => Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
}

internal sealed class NativeTextGenerationPause : IDisposable
{
    private readonly ManualResetEventSlim entered = new();
    private readonly ManualResetEventSlim resume = new();
    private int armed;

    internal void Arm() => Interlocked.Exchange(ref armed, 1);

    internal void Observe(NativeTextFaultStage stage)
    {
        if (stage != NativeTextFaultStage.NativePostingWritten || Interlocked.Exchange(ref armed, 0) != 1)
        {
            return;
        }
        entered.Set();
        if (!resume.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Native generation test barrier expired.");
        }
    }

    internal void WaitUntilEntered(CancellationToken cancellation)
    {
        if (!entered.Wait(TimeSpan.FromSeconds(30), cancellation))
        {
            throw new TimeoutException("Native generation did not reach the posting barrier.");
        }
    }

    internal void Resume() => resume.Set();

    public void Dispose()
    {
        resume.Set();
        entered.Dispose();
        resume.Dispose();
    }
}
