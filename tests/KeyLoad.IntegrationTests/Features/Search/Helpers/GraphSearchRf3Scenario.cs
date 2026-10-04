using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>Seeds an independently specified multi-collection graph for public RF3 search tests.</summary>
internal sealed record GraphSearchRf3Scenario(PartitionRef Partition)
{
    internal const string Documents = "graphdocs";
    internal const string Projects = "graphprojects";
    internal const string Graph = "graphlinks";
    internal const string Root = "root";
    internal const string SecondSeed = "second-seed";
    internal const string Middle = "middle";
    internal const string Alpha = "alpha";
    internal const string Beta = "beta";
    internal const string Gamma = "gamma";
    internal const string Context = "context";
    internal const string Label = "related";
    internal const string LabelFieldGrant = "graph.label.use";
    internal const string SearchGraphTool = "keyload_search_graph";
    internal const int FusionConstant = 60;
    internal const int MaxDepth = 4;
    internal const int MaxVertices = 30;
    internal const int MaxEdges = 60;

    internal static async Task<GraphSearchRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("graph-rf3-tenant-" + Guid.NewGuid().ToString("N"),
            "graph-rf3-database", "graph-rf3-domain", Guid.NewGuid().ToString("N"));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey);
        await ConfigureResourceAsync(admin, partition, Documents, ResourceKind.Collection, cancellationToken);
        await ConfigureResourceAsync(admin, partition, Projects, ResourceKind.Collection, cancellationToken);
        await ConfigureGraphAsync(admin, partition, cancellationToken);
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(Seed(partition), cancellationToken));
        return new(partition);
    }

    internal static GraphSearchRequest Request(PartitionRef partition, int limit = 10,
        ImmutableArray<string>? labels = null, ImmutableArray<string>? allowedIds = null)
    {
        var seeds = ImmutableArray.Create(
            new EntityRef(partition, Projects, Root), new EntityRef(partition, Projects, SecondSeed));
        var walk = new GraphWalkSpec(Graph, seeds, MaxDepth, MaxVertices, MaxEdges, labels);
        var search = new SearchRequest(partition, Documents, Limit: limit, FusionConstant: FusionConstant,
            AllowedIds: allowedIds);
        return new(1, search, Retriever: new(walk));
    }

    internal async Task<McpPersistedIdentity> CreateReaderAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var id = "graph-rf3-reader-" + Guid.NewGuid().ToString("N");
        var principal = new PrincipalRecord(id, Partition.TenantId,
        [
            new("database", Documents, Capability.Query | Capability.DocumentsRead),
            new("database", Projects, Capability.DocumentsRead),
            new("database", Graph, Capability.GraphRead)
        ], []);
        return await ConfigureIdentityAsync(fixture, principal, cancellationToken);
    }

    internal static async Task<McpPersistedIdentity> GrantLabelUseAsync(ClusterFixture fixture,
        McpPersistedIdentity identity, CancellationToken cancellationToken)
    {
        var updated = identity.Principal with
        {
            FieldGrants = [LabelFieldGrant],
            PolicyEpoch = identity.Principal.PolicyEpoch + 1
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(),
            updated, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(updated.PolicyEpoch);
        return identity with { Principal = persisted };
    }

    internal async Task AddReachableDocumentAsync(KeyLoadClient admin, CancellationToken cancellationToken)
    {
        var addition = new CommandRequest(Guid.NewGuid(), Partition,
        [
            new PutDocument(Documents, "delta", "{\"name\":\"delta\"}"),
            new UpsertEdge(Graph, "edge-second-delta", Vertex(Projects, SecondSeed), Vertex(Documents, "delta"), Label)
        ]);
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(addition, cancellationToken));
    }

    internal EntityRef Vertex(string collection, string id) => new(Partition, collection, id);

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
        { FieldPolicies = [new("label", "graph-label", RawUseGrant: LabelFieldGrant)] };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, graph), cancellationToken));
    }

    private static CommandRequest Seed(PartitionRef partition)
    {
        var root = new EntityRef(partition, Projects, Root);
        var second = new EntityRef(partition, Projects, SecondSeed);
        var middle = new EntityRef(partition, Projects, Middle);
        var alpha = new EntityRef(partition, Documents, Alpha);
        var beta = new EntityRef(partition, Documents, Beta);
        var gamma = new EntityRef(partition, Documents, Gamma);
        var context = new EntityRef(partition, Projects, Context);
        return new(Guid.NewGuid(), partition,
        [
            new PutDocument(Projects, Root, "{\"name\":\"root\"}"),
            new PutDocument(Projects, SecondSeed, "{\"name\":\"second\"}"),
            new PutDocument(Projects, Middle, "{\"name\":\"middle\"}"),
            new PutDocument(Projects, Context, "{\"name\":\"context\"}"),
            new PutDocument(Documents, Alpha, "{\"name\":\"alpha\"}"),
            new PutDocument(Documents, Beta, "{\"name\":\"beta\"}"),
            new PutDocument(Documents, Gamma, "{\"name\":\"gamma\"}"),
            new UpsertEdge(Graph, "edge-root-middle", root, middle, Label),
            new UpsertEdge(Graph, "edge-middle-gamma", middle, gamma, Label),
            new UpsertEdge(Graph, "edge-root-beta", root, beta, Label),
            new UpsertEdge(Graph, "edge-second-alpha", second, alpha, Label),
            new UpsertEdge(Graph, "edge-alpha-context", alpha, context, Label),
            new UpsertEdge(Graph, "edge-beta-context", beta, context, Label)
        ]);
    }

    private static async Task<McpPersistedIdentity> ConfigureIdentityAsync(ClusterFixture fixture,
        PrincipalRecord principal, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        var keyId = "graph-rf3-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(keyId, principal.Id, verifier), cancellationToken));
        return new(persisted, new(keyId, principal.Id, verifier), secret);
    }
}
