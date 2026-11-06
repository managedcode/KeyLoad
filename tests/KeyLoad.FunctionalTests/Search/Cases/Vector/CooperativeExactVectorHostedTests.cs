using System.Globalization;
using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Storage.ZoneTree;

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
        var observation = new VectorTurnObservation();
        await using var node = new RequestCqrsHostedRuntimeFixture(observer: observation);
        await node.InitializeAsync(token);
        node.Database.Configure(Collection, ResourceKind.Collection);
        Seed(node.Database);
        using var http = new HttpClient { BaseAddress = node.PublicEndpoint };
        var client = new KeyLoadClient(http, node.AdminKey, UnitClientOptions.Execution());
        observation.Arm(node.Database.Store);
        var request = new SearchRequest(node.Database.Partition, Collection, VectorField: Field, Vector: [1, 0],
            Space: Space, Limit: 1);

        var result = await client.SearchAsync(request, token);
        await Assert.That(result.IsSuccess).IsTrue();
        var hit = result.Value!.Single();
        await Assert.That(hit.Document.Reference.Id).IsEqualTo(DocumentId(Rows - 1));
        await Assert.That(hit.Score).IsEqualTo(request.VectorWeight / (request.FusionConstant + 1d));
        var duringScan = await observation.Turn.WaitAsync(node.Timing.ShutdownTimeout, token);
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

    private sealed class VectorTurnObservation : IGrainRequestPhaseObserver
    {
        private readonly TaskCompletionSource<long> turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Observation? armed;
        private int queued;

        internal Task<long> Turn => turn.Task;

        internal void Arm(ZoneTreeStore store)
            => Volatile.Write(ref armed, new Observation(store, store.GetReadDiagnostics().RangeBaselineEntries));

        public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
            IGrainContext context, CancellationToken token)
        {
            if (phase == GrainRequestPhase.RequestStarted && identity.ReadKind == GrainReadKind.Search
                && Volatile.Read(ref armed) is { } observation && context.GrainInstance is RequestGrain
                && Interlocked.Exchange(ref queued, 1) == 0)
            {
                context.Scheduler.QueueAction(() => ObserveTurn(context, observation, token));
            }
            return ValueTask.CompletedTask;
        }

        public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
        {
            if (identity.ReadKind == GrainReadKind.Search && queued != 0 && !turn.Task.IsCompleted)
            {
                turn.TrySetException(new InvalidOperationException("The exact vector read completed before its observed scheduler turn."));
            }
        }

        private void ObserveTurn(IGrainContext context, Observation observation, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                turn.TrySetCanceled(token);
                return;
            }
            var examined = observation.Examined;
            if (examined >= ZoneTreeRangeReader.CooperativeSteps)
            {
                turn.TrySetResult(examined);
                return;
            }
            context.Scheduler.QueueAction(() => ObserveTurn(context, observation, token));
        }

        private sealed record Observation(ZoneTreeStore Store, long Baseline)
        {
            internal long Examined => Store.GetReadDiagnostics().RangeBaselineEntries - Baseline;
        }
    }
}
