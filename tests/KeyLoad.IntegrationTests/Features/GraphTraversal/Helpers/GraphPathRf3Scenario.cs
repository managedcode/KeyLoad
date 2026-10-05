using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRf3Scenario
{
    internal const string CollectionA = "path-orders";
    internal const string CollectionB = "path-projects";
    internal const string Graph = "path-links";
    internal const string Label = "walk";
    internal const string RestrictedLabel = "restricted";
    internal const string LabelUseGrant = "graph.path.label.use";
    internal const string LabelReadGrant = "graph.path.label.read";
    internal const string SecretReadGrant = "graph.path.secret.read";
    internal const string SecretMarker = "graph-path-private-marker";
    internal const string DirectTool = "keyload_graph_shortest_path";
    internal const string SqlTool = "keyload_query_graph_path";
    internal const string SourceId = "source";
    internal const string OverlapId = "same-id";
    internal const string TargetId = "target";
    internal const string HiddenSourceId = "hidden-source";
    internal const string HiddenMiddleId = "hidden-middle";
    internal const string HiddenTargetId = "hidden-target";
    internal const string IsolatedId = "isolated";
    internal const string SecretOwner = "different-row-owner";
    internal const string PublicAttributes = "{\"secret\":\"graph-path-private-marker\",\"public\":\"visible\"}";
    internal const string EmptyAttributes = "{}";

    internal static async Task<GraphPathRf3Seed> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("graph-path-tenant-" + Guid.NewGuid().ToString("N"),
            "graph-path-database", "graph-path-domain", Guid.NewGuid().ToString("N"));
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey);
        await ConfigureResourceAsync(admin, partition, CollectionA, ResourceKind.Collection, cancellationToken);
        await ConfigureResourceAsync(admin, partition, CollectionB, ResourceKind.Collection, cancellationToken);
        await ConfigureGraphAsync(admin, partition, cancellationToken);
        var identity = await CreateReaderAsync(admin, partition, cancellationToken);
        var refs = CreateReferences(partition);
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(Seed(partition, refs, identity.Principal.Id), cancellationToken));
        return new(partition, refs.Source, refs.FirstBranch, refs.SecondBranch, refs.Target,
            refs.HiddenSource, refs.HiddenIntermediate, refs.HiddenTarget, refs.Isolated, identity);
    }

    internal static GraphShortestPathRequest Request(GraphPathRf3Seed seed, EntityRef? from = null,
        EntityRef? to = null, int maxDepth = 16, int maxVertices = 1_000, int maxEdges = 5_000,
        ImmutableArray<string>? labels = null)
        => new(1, seed.Partition, Graph, from ?? seed.Source, to ?? seed.Target,
            maxDepth, maxVertices, maxEdges, labels);

    internal static SqlGraphPathRequest SqlRequest(GraphPathRf3Seed seed, EntityRef? from = null,
        EntityRef? to = null, int maxDepth = 16, int maxVertices = 1_000, int maxEdges = 5_000,
        string? label = null)
    {
        var start = from ?? seed.Source;
        var finish = to ?? seed.Target;
        var statement = $"SELECT * FROM GRAPH_SHORTEST_PATH('{Graph}', '{start.Collection}', '{start.Id}', "
            + $"'{finish.Collection}', '{finish.Id}', {maxDepth}, {maxVertices}, {maxEdges}"
            + (label is null ? ")" : $", '{label}')");
        return new(1, new QueryRequest(seed.Partition, statement, AllowFullScan: true));
    }

    internal static SqlGraphPathRequest SqlRequestInPartition(GraphPathRf3Seed seed, PartitionRef partition)
    {
        var request = SqlRequest(seed);
        return request with { Query = request.Query with { Partition = partition } };
    }

    internal static async Task<McpPersistedIdentity> GrantLabelUseAsync(ClusterFixture fixture,
        GraphPathRf3Seed seed, CancellationToken cancellationToken)
    {
        var updated = seed.Reader.Principal with
        {
            FieldGrants = [LabelUseGrant, LabelReadGrant],
            PolicyEpoch = checked(seed.Reader.Principal.PolicyEpoch + 1)
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(
            Guid.NewGuid(), updated, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(updated.PolicyEpoch);
        return seed.Reader with { Principal = persisted };
    }

    internal static async Task<McpPersistedIdentity> ReplaceGrantsAsync(ClusterFixture fixture,
        McpPersistedIdentity identity, ScopeGrant[] grants, string[] fieldGrants,
        CancellationToken cancellationToken)
    {
        var updated = identity.Principal with
        {
            Grants = [.. grants],
            FieldGrants = [.. fieldGrants],
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1)
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(
            Guid.NewGuid(), updated, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(updated.PolicyEpoch);
        return identity with { Principal = persisted };
    }

    internal static ScopeGrant[] FullGraphGrants(PartitionRef partition)
        =>
        [
            new(partition.DatabaseId, CollectionA, Capability.DocumentsRead | Capability.Query),
            new(partition.DatabaseId, CollectionB, Capability.DocumentsRead | Capability.Query),
            new(partition.DatabaseId, Graph, Capability.GraphRead | Capability.Query)
        ];

    private static async Task ConfigureResourceAsync(KeyLoadClient admin, PartitionRef partition,
        string name, ResourceKind kind, CancellationToken cancellationToken)
    {
        var resource = new ResourceDefinition(name, kind, partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), cancellationToken));
    }

    private static async Task ConfigureGraphAsync(KeyLoadClient admin, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var graph = new ResourceDefinition(Graph, ResourceKind.Graph, partition.TransactionDomainId)
        {
            FieldPolicies =
            [
                new("/label", "graph-path-label", RawReadGrant: LabelReadGrant, RawUseGrant: LabelUseGrant),
                new("/secret", "graph-path-private", RawReadGrant: SecretReadGrant)
            ]
        };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, graph), cancellationToken));
    }

    private static async Task<McpPersistedIdentity> CreateReaderAsync(KeyLoadClient admin,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var principalId = "graph-path-reader-" + Guid.NewGuid().ToString("N");
        var principal = new PrincipalRecord(principalId, partition.TenantId, [.. FullGraphGrants(partition)], [LabelReadGrant])
        { RestrictRows = true, OwnerId = principalId };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, cancellationToken));
        var keyId = "graph-path-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var credential = new ApiKeyRecord(keyId, principalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureApiKeyAsync(Guid.NewGuid(), credential, cancellationToken));
        return new(persisted, credential, secret);
    }

    private static GraphPathRf3References CreateReferences(PartitionRef partition)
    {
        EntityRef Ref(string collection, string id) => new(partition, collection, id);
        return new(Ref(CollectionA, SourceId), Ref(CollectionA, OverlapId), Ref(CollectionB, OverlapId),
            Ref(CollectionB, TargetId), Ref(CollectionA, HiddenSourceId), Ref(CollectionB, HiddenMiddleId),
            Ref(CollectionB, HiddenTargetId), Ref(CollectionA, IsolatedId));
    }

    private static CommandRequest Seed(PartitionRef partition, GraphPathRf3References refs, string ownerId)
        => new(Guid.NewGuid(), partition,
        [
            Put(CollectionA, SourceId, ownerId), Put(CollectionA, OverlapId, ownerId),
            Put(CollectionB, OverlapId, ownerId), Put(CollectionB, TargetId, ownerId),
            Put(CollectionA, HiddenSourceId, SecretOwner), Put(CollectionB, HiddenMiddleId, SecretOwner),
            Put(CollectionB, HiddenTargetId, SecretOwner), Put(CollectionA, IsolatedId, ownerId),
            Edge("a-branch-a", refs.Source, refs.FirstBranch, Label),
            Edge("z-branch-b", refs.Source, refs.SecondBranch, Label),
            Edge("a-end-a", refs.FirstBranch, refs.Target, Label),
            Edge("b-end-b", refs.SecondBranch, refs.Target, Label),
            Edge("z-shortcut", refs.Source, refs.Target, "shortcut"),
            Edge("c-cycle", refs.FirstBranch, refs.Source, Label),
            Edge("d-self", refs.Source, refs.Source, Label),
            Edge("hidden-source-edge", refs.HiddenSource, refs.Target, RestrictedLabel),
            Edge("hidden-middle-first", refs.Source, refs.HiddenIntermediate, RestrictedLabel),
            Edge("hidden-middle-second", refs.HiddenIntermediate, refs.Isolated, RestrictedLabel),
            Edge("hidden-target-edge", refs.Source, refs.HiddenTarget, RestrictedLabel)
        ]);

    private static PutDocument Put(string collection, string id, string owner)
        => new(collection, id, EmptyAttributes, Access: new(owner));

    private static UpsertEdge Edge(string id, EntityRef from, EntityRef to, string label)
        => new(Graph, id, from, to, label, id == "a-end-a" ? PublicAttributes : EmptyAttributes);
}
