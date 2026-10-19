using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Seeds both canonical partitions through the actual two-copy Orleans command owner.</summary>
internal sealed class QueryHostedSearchDataset(RequestCqrsHostedCluster cluster)
{
    internal const int Copies = 2;
    private const int Documents = 2;
    internal const int Version = 1;
    internal const int FusionConstant = 60;
    internal const string Reader = "query-hosted-reader";
    internal const string Collection = "query-hosted-documents";
    internal const string DocumentId = "one";
    internal const string Title = "/title";
    internal const string Secret = "/secret";
    internal const string DestinationJson = "{\"title\":\"destination\",\"secret\":\"destination-private-canary\"}";
    internal const string SourceJson = "{\"title\":\"source\",\"secret\":\"source-private-canary\"}";
    internal const string DestinationProjected = "{\"title\":\"destination\"}";
    internal const string SourceProjected = "{\"title\":\"source\"}";
    internal PartitionRef First => cluster.Nodes[0].Database.Partition;
    internal PartitionRef Second => First with { PartitionKey = "customer-2" };
    internal ResourceLifetime Creation { get; private set; } = null!;
    internal PrincipalRecord Principal { get; private set; } = null!;
    internal DistributedSearchRequest Request => new(Version, [First, Second],
        new(First, Collection, TextField: Title, Text: "source destination", Limit: Documents,
            FusionConstant: FusionConstant));

    internal async Task SeedAsync(CancellationToken token)
    {
        var owner = cluster.Nodes[0].Database;
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, First.TransactionDomainId)
        {
            FieldPolicies = [new(Secret, "private")]
        };
        var resource = new ConfigureResourceRequest(First.TenantId, First.DatabaseId, definition);
        var creation = owner.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(),
            RequestCqrsHostedProfile.Root, owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(resource));
        _ = (await owner.Coordinator.SubmitVerifiedAsync(creation, token).ConfigureAwait(false)).Get<ResourceDefinition>();
        Creation = ResourceLifetimeIdentity.Create(creation, resource.TenantId, resource.DatabaseId, definition);
        Principal = new(Reader, First.TenantId,
            [new(First.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []);
        _ = owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(Principal),
            cancellationToken: token).Get<PrincipalRecord>();
        await WriteAsync(First, DestinationJson, token).ConfigureAwait(false);
        await WriteAsync(Second, SourceJson, token).ConfigureAwait(false);
    }

    private async Task WriteAsync(PartitionRef partition, string json, CancellationToken token)
    {
        var owner = cluster.Nodes[0].Database;
        var commandId = Guid.NewGuid();
        var expectedPosition = checked(owner.ReadCanonicalCut().AppliedPosition + Version);
        var receipt = owner.Submit(OperationKind.Batch,
            new CommandRequest(commandId, partition, [new PutDocument(Collection, DocumentId, json)]),
            id: commandId, cancellationToken: token).Get<CommitReceipt>();
        var expected = new CommitReceipt(commandId,
            new(owner.Store.Identity.Incarnation, partition.AtomicPartitionId, expectedPosition),
            [new("putDocument", Collection, DocumentId, Version)], DurabilityProfile.ProcessDurable, Copies);
        await Assert.That(OrleansSerialization.Serialize(receipt).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }
}
