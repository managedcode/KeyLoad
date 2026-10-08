using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteDocumentNativeFixture : IDisposable
{
    internal const string Root = "root";
    internal const string Reader = "remote-reader";
    internal const string Tenant = "remote-tenant";
    internal const string Collection = "remote-documents";
    internal const string DocumentId = "one";
    private const string PartitionKey = "one";
    internal const string Title = "/title";
    internal const string Secret = "/secret";
    internal const string Classification = "private";
    internal const string OriginalJson = "{\"title\":\"destination\",\"secret\":\"private-canary\"}";
    internal const string ProjectedJson = "{\"title\":\"destination\"}";
    internal const int Version = 1;
    internal const long Empty = 0;
    internal const long First = 1;
    internal const long Second = 2;
    internal static readonly PartitionRef Partition = new(Tenant, "remote-db", "remote-domain", PartitionKey);
    internal static readonly EntityRef Reference = new(Partition, Collection, DocumentId);
    internal static readonly Guid WriteId = Guid.Parse("102b95e5-2657-40bb-9d1a-efc39b18877d");
    internal static readonly Guid BindId = Guid.Parse("651fa2f2-67e0-4193-b880-94a3a3f8cf41");
    internal PhysicalShardCatalogFixture Source { get; }
    internal PhysicalShardCatalogFixture Destination { get; }
    internal ReplicatedOperation Write { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;

    internal RemoteDocumentNativeFixture()
    {
        Source = new(PhysicalOwnerDirectoryWholeFlow.Control.Owner.Incarnation,
            PhysicalOwnerDirectoryWholeFlow.Control.Owner);
        var failures = new List<Exception>();
        PhysicalShardCatalogFixture? destination = null;
        ServerFailureObserver.Observe(() => destination = new(PhysicalOwnerDirectoryWholeFlow.Destination.Owner.Incarnation,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner), failures);
        if (failures.Count != 0)
        {
            ServerFailureObserver.Observe(Source.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        Destination = destination ?? throw new InvalidOperationException("The destination native fixture was not created.");
    }

    internal void Seed()
    {
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(Source);
        Require(Destination.Bootstrap(new(Version, Empty, PhysicalOwnerDirectoryWholeFlow.Destination.Owner.PhysicalShardId,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner.Incarnation, PhysicalOwnerDirectoryWholeFlow.Destination.Owner.VoterIds)));
        Require(Source.Database.Apply(PhysicalOwnerDirectoryWholeFlow.Operation(Source,
            PhysicalOwnerDirectoryWholeFlow.Request(), PhysicalOwnerDirectoryWholeFlow.RegistrationId)));
        Configure(Source);
        Configure(Destination);
        Source.AddPrincipal(new(Reader, Tenant,
            [new(Partition.DatabaseId, Collection, Capability.DocumentsRead)], [Title]));
        Destination.AddPrincipal(new(Reader, Tenant, [], [Title]));
        Write = Operation(Destination, OperationKind.Batch, WriteId,
            new CommandRequest(WriteId, Partition, [new PutDocument(Collection, DocumentId, OriginalJson)]));
        Receipt = Require(Destination.Database.Apply(Write, First)).Get<CommitReceipt>();
        Require(Source.Database.Apply(Operation(Source, OperationKind.BindAtomicPartitionPlacement, BindId,
            new BindAtomicPartitionPlacementRequest(Version, Empty, Partition,
                PhysicalOwnerDirectoryWholeFlow.Destination.Owner.PhysicalShardId))));
    }

    internal static ReplicatedOperation Operation<T>(PhysicalShardCatalogFixture fixture,
        OperationKind kind, Guid id, T payload)
        => fixture.Database.CreateNativeOperation(kind, id, Root,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));

    internal void GrantDestination() => Destination.AddPrincipal(new(Reader, Tenant,
        [new(Partition.DatabaseId, Collection, Capability.DocumentsRead)], [Title])
    { PolicyEpoch = Second });

    private static void Configure(PhysicalShardCatalogFixture fixture)
        => Require(fixture.Database.Apply(Operation(fixture, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                new(Collection, ResourceKind.Collection, Partition.TransactionDomainId)
                { FieldPolicies = [new(Secret, Classification)] }))));

    private static OperationResult Require(OperationResult result)
        => result.Error is null ? result : throw new InvalidOperationException(result.SafeDetail);

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Destination.Dispose, failures);
        ServerFailureObserver.Observe(Source.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
