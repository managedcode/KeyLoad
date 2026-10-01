using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using KeyLoad;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

[MemoryDiagnoser]
public class EmbeddedBenchmarks
{
    private string directory = "";
    private ZoneTreeStore store = null!;
    private DatabaseEngine database = null!;
    private readonly PartitionRef partition = new("benchmark", "database", "documents", "1");
    [GlobalSetup]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "keyload-benchmark-" + Guid.NewGuid().ToString("N"));
        store = new(new(directory)); database = new(store, new AuthorizationPolicy());
        database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential("root", "root", "root.benchmark-credential-32-characters"));
        Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest("benchmark", "database", new("documents", ResourceKind.Collection, "documents")));
        Submit(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), partition, [new PutDocument("documents", "1", "{\"text\":\"clustered document database\",\"number\":42}") ]));
    }
    private void Submit<T>(OperationKind kind, T payload)
    {
        var id = payload is CommandRequest command ? command.CommandId : Guid.NewGuid();
        var result = database.Apply(new(id, kind, "root", DateTimeOffset.UtcNow, System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options)));
        if (result.Error is { } error) throw Errors.Fail(error, result.SafeDetail!);
    }
    [Benchmark] public DocumentResult? PointRead() => database.GetDocument("root", new(partition, "documents", "1"));
    [Benchmark] public byte[] CompositeKey() => KeyLoad.Storage.KeyCodec.Encode("tenant", "database", "partition", 42m, "document");
    [Benchmark] public double ExactCosine() => SearchEngine.Similarity([1, 2, 3, 4, 5, 6, 7, 8], [8, 7, 6, 5, 4, 3, 2, 1], DistanceMetric.Cosine);
    [GlobalCleanup] public void Cleanup() { store.Dispose(); Directory.Delete(directory, true); }
}
