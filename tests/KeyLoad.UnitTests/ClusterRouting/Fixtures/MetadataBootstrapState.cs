using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class MetadataBootstrapState : IDisposable
{
    internal const string Actor = "metadata-bootstrap-actor";
    internal const string Secret = "metadata-bootstrap-key.fixture-credential-32-characters";
    internal const string Collection = "metadata-documents";
    internal const string Streams = "metadata-events";
    internal const string Queue = "metadata-queue";
    internal const string Entity = "bootstrapped";
    private const string PartitionKey = "metadata-bucket";
    internal const string Json = "{\"metadata\":true}";
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly Guid FirstShard = Guid.Parse("019ba001-0000-4000-8000-000000000001");
    private static readonly Guid SecondShard = Guid.Parse("019ba001-0000-4000-8000-000000000002");
    internal PhysicalShardCatalogFixture Fixture
    {
        get;
    }
    internal PartitionRef Partition { get; } = new("metadata-tenant", "metadata-db", "metadata-domain", PartitionKey);
    internal BootstrapPhysicalShardCatalogRequest[] Requests
    {
        get;
    }
    internal MetadataBootstrapState(bool conflicting)
    {
        Conflicting = conflicting;
        Fixture = new();
        var incarnation = Fixture.Store.Identity.Incarnation;
        Requests = [new(1, 0L, FirstShard, incarnation, PhysicalShardCatalogVoterIds.Standard),
            new(1, 0L, conflicting ? SecondShard : FirstShard, incarnation, PhysicalShardCatalogVoterIds.Standard)];
    }
    internal ReplicatedOperation[] Originals { get; set; } = [];
    internal OperationResult[] Results { get; set; } = [];
    internal byte[][] Outcomes { get; set; } = [];
    internal int Winner
    {
        get; set;
    }
    internal bool Conflicting
    {
        get;
    }
    internal ReplicatedOperation Fresh { get; set; } = null!;
    internal byte[] FreshOutcome { get; set; } = [];
    internal ReplicatedOperation Command { get; set; } = null!;
    internal CommandRequest Request { get; set; } = null!;
    internal CommitReceipt Receipt { get; set; } = null!;
    internal byte[] CommandOutcome { get; set; } = [];
    internal string[] Image { get; set; } = [];
    internal long Position
    {
        get; set;
    }
    internal StoreIdentity Identity { get; set; } = null!;
    internal static PrincipalRecord Principal(long epoch, bool admin) => new(Actor, "system", [new("*", "*", Capability.All)], ["*"])
    {
        PolicyEpoch = epoch,
        ClusterAdministrator = admin
    };
    internal ReplicatedOperation Operation<T>(OperationKind kind, T request, string principal = Actor, DateTimeOffset? time = null)
        => Fixture.Database.CreateNativeOperation(kind, Guid.NewGuid(), principal,
            time ?? Fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
    public void Dispose() => Fixture.Dispose();
}
