using System.Diagnostics;
using KeyLoad.Client;
using ManagedCode.Communication;

namespace KeyLoad.Comparisons.Targets;

public sealed class KeyLoadTarget(HttpClient http, string apiKey, string runId) : IComparisonTarget
{
    private readonly KeyLoadClient client = new(http, apiKey);
    private readonly PartitionRef partition = new("benchmark-" + runId, "comparison", "workload", "shared");
    private VectorSpace space = null!;
    private int topK;
    public TargetProfile Profile { get; private set; } = new("KeyLoad", "0.1.0-dev", "3 voters, RF3, one physical shard; all processes on one host",
        "QuorumProcessDurable; process-kill qualified, power-loss unqualified", "strong quorum barrier", "HTTP JSON", "authenticated root, all grants", null);
    public bool Supports(Scenario scenario) => true;
    private static T Success<T>(Result<T> result)
    {
        if (!result.IsSuccess) throw new ComparisonFailure("KeyLoad:" + result.Problem?.ErrorCode);
        return result.Value!;
    }
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        var status = Success(await client.StatusAsync(cancellationToken));
        if (status.Voters != 3 || status.Durability != DurabilityProfile.QuorumProcessDurable)
            throw new ComparisonFailure("KeyLoadRf3Required");
        space = new("comparison", dataset.Options.Dimensions, DistanceMetric.Cosine, "seeded-float32", "1");
        topK = dataset.Options.TopK;
        foreach (var (name, kind) in new[] { ("documents", ResourceKind.Collection), ("jobs", ResourceKind.WorkQueue) })
            Success(await client.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, new(name, kind, partition.TransactionDomainId)), cancellationToken));
        foreach (var document in dataset.Documents)
            Success(await client.CommitAsync(new(Guid.NewGuid(), partition,
                [new PutDocument("documents", document.Id, document.Json, 0), new PutVector("documents", document.Id, "/embedding", document.Vector, space, 1)]), cancellationToken));
    }
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(new Session(this));
    public ValueTask DisposeAsync() { http.Dispose(); return ValueTask.CompletedTask; }

    private sealed class Session(KeyLoadTarget target) : IComparisonSession
    {
        public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        {
            var found = Success(await target.client.GetAsync(new(target.partition, "documents", document.Id), cancellationToken));
            return found is null ? null : new(found.Reference.Id, found.Json);
        }
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            switch (scenario)
            {
                case Scenario.PointRead: return new(Document: await ReadAsync(document, cancellationToken));
                case Scenario.DocumentWrite:
                    var receipt = Success(await target.client.CommitAsync(new(Guid.NewGuid(), target.partition,
                        [new PutDocument("documents", document.Id, document.Json, 0)]), cancellationToken));
                    if (receipt.Durability != DurabilityProfile.QuorumProcessDurable) throw new ComparisonFailure("WrongWriteProfile");
                    return new();
                case Scenario.VectorExact:
                    var neighbors = Success(await target.client.SearchAsync(new(target.partition, "documents", VectorField: "/embedding",
                        Vector: document.Vector, Space: target.space, Limit: target.topK), cancellationToken));
                    return new(Neighbors: neighbors.Select(item => new FoundDocument(item.Document.Reference.Id, item.Document.Json)).ToArray());
                case Scenario.QueueCycle:
                    var lane = new QueueLaneRef(target.partition, "jobs");
                    var begin = Stopwatch.GetTimestamp();
                    Success(await target.client.CommitAsync(new(Guid.NewGuid(), target.partition,
                        [new EnqueueMessage("jobs", document.Id, document.Json)]), cancellationToken));
                    var enqueued = Stopwatch.GetTimestamp();
                    Delivery? delivery = null;
                    while (delivery is null)
                    {
                        delivery = Success(await target.client.ReceiveAsync(new(Guid.NewGuid(), lane), cancellationToken)).Deliveries.SingleOrDefault();
                        if (delivery is null) await Task.Delay(1, cancellationToken);
                    }
                    var received = Stopwatch.GetTimestamp();
                    Success(await target.client.CompleteAsync(new(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack), cancellationToken));
                    return new(Message: new(delivery.Id, delivery.PayloadJson), Queue: new(
                        Stopwatch.GetElapsedTime(begin, enqueued).TotalMilliseconds, Stopwatch.GetElapsedTime(enqueued, received).TotalMilliseconds,
                        Stopwatch.GetElapsedTime(received).TotalMilliseconds));
                default: throw new NotSupportedException();
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
