using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class HeavyRouteAdmissionFixture : IDisposable
{
    internal const string Collection = "admission-vertices";
    internal const string Graph = "admission-graph";
    internal const string Reader = "admission-reader";
    internal const string Secret = "admission-reader.actual-persisted-credential-32-characters";
    internal const string ReadGrant = "label.read";
    internal const string UseGrant = "label.use";
    internal TestDatabase Harness { get; } = new();
    internal CommandRequest Seed { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;
    internal byte[] Outcome { get; private set; } = null!;
    internal EntityRef Vertex(string id) => new(Harness.Partition, Collection, id);
    internal EdgeRecord Edge => new("ab", Vertex("a"), Vertex("b"), "route", "{}", 1L);
    internal GraphShortestPathRequest Path => new(1, Harness.Partition, Graph, Vertex("a"), Vertex("b"), 1, 2, 1, ["route"]);
    internal async Task InitializeAsync()
    {
        Harness.Configure(Collection, ResourceKind.Collection);
        Harness.Configure(Graph, ResourceKind.Graph, fields: [new("/label", "restricted", ReadGrant, UseGrant)]);
        Seed = new(Guid.NewGuid(), Harness.Partition,
            [new PutDocument(Collection, "a", "{}"), new PutDocument(Collection, "b", "{}"),
             new UpsertEdge(Graph, Edge.Id, Edge.From, Edge.To, Edge.Label, Edge.AttributesJson)]);
        var cut = Harness.Store.Position;
        Receipt = Harness.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>();
        var expected = new CommitReceipt(Seed.CommandId, new(Harness.Store.Identity.Incarnation, Harness.Partition.AtomicPartitionId, cut + 1L), [new("putDocument", Collection, "a", 1L), new("putDocument", Collection, "b", 1L), new("upsertEdge", Graph, "ab", 1L)], DurabilityProfile.ProcessDurable, 1);
        await HeavyRouteAdmissionReads.EqualAsync(Receipt, expected);
        await RuntimeReceiptAsync(Receipt);
        await Assert.That(Harness.Store.Position).IsEqualTo(cut + 1L);
        Outcome = Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", Seed.CommandId)))
            ?? throw new InvalidOperationException("The original admission source outcome must exist.");
        await PersistAsync(false, 1L);
        Harness.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(AuthorizationExecution.Credential(Reader, Reader, Secret))).Get<bool>();
    }
    internal async Task PersistAsync(bool use, long epoch)
    {
        var expected = new PrincipalRecord(Reader, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, Collection, Capability.DocumentsRead), new(Harness.Partition.DatabaseId, Graph, Capability.GraphRead)],
            use ? [ReadGrant, UseGrant] : [ReadGrant])
        {
            PolicyEpoch = epoch
        };
        await HeavyRouteAdmissionReads.EqualAsync(Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>(), expected);
        await HeavyRouteAdmissionReads.EqualAsync(Harness.Store.Read(view =>
            view.GetRecord<PrincipalRecord>(KeySpace.Principal(Reader))), expected);
    }
    internal static async Task RuntimeReceiptAsync(CommitReceipt receipt)
    {
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
    public void Dispose() => Harness.Dispose();
}
