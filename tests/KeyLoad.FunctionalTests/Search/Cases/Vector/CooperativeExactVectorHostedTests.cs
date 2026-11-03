using System.Globalization;
using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class CooperativeExactVectorHostedTests
{
    private const int Rows = 513;
    private const int BatchRows = 64;
    private const string Collection = "cooperative-vectors";
    private const string Field = "/embedding";
    private const string Json = "{}";
    private const string IdFormat = "D4";
    private static readonly VectorSpace Space = new("semantic", 2, DistanceMetric.DotProduct, "test-embedding", "current");

    [Test]
    public async Task PublicExactSearchLetsTheRequestSchedulerRunBeforeReturningTheOracleWinner()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var clock = new VectorTurnClock();
        await using var node = new RequestCqrsHostedRuntimeFixture(clock: clock);
        await node.InitializeAsync(token);
        node.Database.Configure(Collection, ResourceKind.Collection);
        Seed(node.Database);
        using var http = new HttpClient { BaseAddress = node.PublicEndpoint };
        var client = new KeyLoadClient(http, node.AdminKey, UnitClientOptions.Execution());
        clock.Arm(node.Database.Store, node.Services.GetRequiredService<IGrainContextAccessor>());
        var request = new SearchRequest(node.Database.Partition, Collection, VectorField: Field, Vector: [1, 0],
            Space: Space, Limit: 1);

        var result = await client.SearchAsync(request, token);
        await Assert.That(result.IsSuccess).IsTrue();
        var hit = result.Value!.Single();
        await Assert.That(hit.Document.Reference.Id).IsEqualTo(DocumentId(Rows - 1));
        await Assert.That(hit.Score).IsEqualTo(request.VectorWeight / (request.FusionConstant + 1d));
        var duringScan = await clock.Turn.WaitAsync(node.Timing.ShutdownTimeout, token);
        await Assert.That(duringScan).IsGreaterThanOrEqualTo((long)ZoneTreeRangeReader.CooperativeSteps);
        await Assert.That(duringScan).IsLessThan(Rows);
    }

    private static void Seed(RequestCqrsHostedDatabase database)
    {
        foreach (var batch in Enumerable.Range(0, Rows).Chunk(BatchRows))
        {
            var writes = new List<Mutation>(batch.Length * 2);
            foreach (var row in batch)
            {
                var id = DocumentId(row);
                writes.Add(new PutDocument(Collection, id, Json));
                writes.Add(new PutVector(Collection, id, Field, row == Rows - 1 ? [2, 0] : [1, 0], Space, 1));
            }
            database.Commit([.. writes]);
        }
    }

    private static string DocumentId(int row) => row.ToString(IdFormat, CultureInfo.InvariantCulture);

    private sealed class VectorTurnClock : TimeProvider
    {
        private readonly TaskCompletionSource<long> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Observation? armed;
        private int queued;

        internal Task<long> Turn => turn.Task;

        internal void Arm(ZoneTreeStore store, IGrainContextAccessor context)
            => Volatile.Write(ref armed, new Observation(store, context, store.GetReadDiagnostics().RangeBaselineEntries));

        public override long GetTimestamp()
        {
            if (Volatile.Read(ref armed) is { } observation && Volatile.Read(ref queued) == 0
                && observation.Context.GrainContext is { GrainInstance: RequestGrain } grain
                && observation.Examined > 0 && Interlocked.Exchange(ref queued, 1) == 0)
            {
                grain.Scheduler.QueueAction(() => turn.SetResult(observation.Examined));
            }
            return TimeProvider.System.GetTimestamp();
        }

        private sealed record Observation(ZoneTreeStore Store, IGrainContextAccessor Context, long Baseline)
        {
            internal long Examined => Store.GetReadDiagnostics().RangeBaselineEntries - Baseline;
        }
    }
}
