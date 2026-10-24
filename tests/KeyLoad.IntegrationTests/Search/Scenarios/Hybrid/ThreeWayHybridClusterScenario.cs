using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Search;

internal sealed record ThreeWayHybridClusterScenario(PartitionRef Partition)
{
    internal const string Documents = "three-way-documents";
    internal const string Projects = "three-way-projects";
    internal const string Graph = "three-way-graph";
    internal const string ExpansionGraph = "three-way-expansion-graph";
    internal const string TextField = "/body";
    internal const string VectorField = "/embedding";
    internal const string TextGrant = "three-way.text.use";
    internal const string VectorGrant = "three-way.vector.use";
    internal const string LabelGrant = "three-way.graph.label.use";
    internal const string A = "a";
    internal const string B = "b";
    internal const string C = "c";
    internal const string D = "d";
    internal const string E = "e";
    internal const string RetrieveRoot = "retrieve-root";
    internal const string ScopeRoot = "scope-root";
    internal const string SearchGraphTool = "keyload_search_graph";
    internal const string SqlGraphTool = "keyload_query_search";
    internal const string SpaceName = "three-way-space";
    internal const string Model = "three-way-model";
    internal const string Version = "1";
    private const string VectorParameter = "vector";
    internal const int FusionConstant = 10;
    internal const double TextWeight = 2;
    internal const double VectorWeight = 1;
    internal const double GraphWeight = 13d / 132d;
    internal const string SqlGraphWeight = "0.09848484848484848";
    internal static VectorSpace Space { get; } = new(SpaceName, 2, DistanceMetric.Cosine, Model, Version);

    internal static async Task<ThreeWayHybridClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("three-way-tenant-" + Guid.NewGuid().ToString("N"),
            "three-way-database", "three-way-domain", Guid.NewGuid().ToString("N"));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureAsync(admin, partition, DocumentsResource(), cancellationToken);
        await ConfigureAsync(admin, partition, new(Projects, ResourceKind.Collection, partition.TransactionDomainId), cancellationToken);
        await ConfigureAsync(admin, partition, GraphResource(), cancellationToken);
        await ConfigureAsync(admin, partition, new(ExpansionGraph, ResourceKind.Graph, partition.TransactionDomainId), cancellationToken);
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(ThreeWayHybridClusterSeed.Create(partition), cancellationToken));
        return new(partition);
    }

    internal GraphSearchRequest Request(bool restricted = false, bool expansion = true, bool allZero = false, bool labeled = false)
    {
        var allowed = restricted ? ImmutableArray.Create(B, C, D) : (ImmutableArray<string>?)null;
        var search = new SearchRequest(Partition, Documents, TextField, "signal", VectorField, [1, 0], Space,
            Limit: 10, TextWeight: allZero ? 0 : TextWeight, VectorWeight: allZero ? 0 : VectorWeight,
            FusionConstant: FusionConstant, AllowedIds: allowed);
        var retrieveWalk = Walk(RetrieveRoot, 4) with
        {
            Labels = labeled ? ImmutableArray.Create("related") : null
        };
        return new(1, search,
            Scope: restricted ? new(Walk(ScopeRoot, 2)) : null,
            Retriever: new(retrieveWalk, allZero ? 0 : GraphWeight),
            Expansion: expansion ? new(ExpansionGraph, MaxDepth: 1, MaxVertices: 20, MaxEdges: 40) : null);
    }

    internal SqlGraphSearchRequest SqlRequest(bool restricted = false, bool expansion = true, bool allZero = false, bool labeled = false)
    {
        var scope = restricted
            ? "GRAPH_SCOPE('three-way-graph',GRAPH_SEEDS(ENTITY('three-way-projects','scope-root')),2,30,60,NULL)" : "NULL";
        var labels = labeled ? "GRAPH_LABELS('related')" : "NULL";
        var expanded = expansion ? "GRAPH_EXPANSION('three-way-expansion-graph',1,20,40,NULL)" : "NULL";
        var allowed = restricted ? "GRAPH_IDS('b','c','d')" : allZero ? "GRAPH_IDS()" : "NULL";
        var statement = "SELECT * FROM GRAPH_SEARCH('three-way-documents', TEXT_MATCH('/body','signal',"
            + (allZero ? "0" : "2") + "), VECTOR_MATCH('/embedding',@vector,"
            + "VECTOR_SPACE('three-way-space',2,'Cosine','three-way-model','1')," + (allZero ? "0" : "1") + "), "
            + scope + ", GRAPH_RETRIEVER('three-way-graph',GRAPH_SEEDS(ENTITY('three-way-projects','retrieve-root')),"
            + "4,30,60," + labels + "," + (allZero ? "0" : SqlGraphWeight) + "), "
            + expanded + ", " + allowed + ", 10, 10)";
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [VectorParameter] = JsonSerializer.SerializeToElement(new[] { 1f, 0f })
        };
        return new(1, new(Partition, statement, parameters, AllowFullScan: true));
    }

    internal async Task<McpPersistedIdentity> CreateReaderAsync(ClusterFixture fixture, bool vectorGrant,
        CancellationToken cancellationToken, bool labelGrant = false)
    {
        var grants = ImmutableArray.CreateBuilder<string>();
        grants.Add(TextGrant);
        if (vectorGrant)
        {
            grants.Add(VectorGrant);
        }
        if (labelGrant)
        {
            grants.Add(LabelGrant);
        }
        var principal = new PrincipalRecord("three-way-reader-" + Guid.NewGuid().ToString("N"), Partition.TenantId,
        [
            new(Partition.DatabaseId, Documents,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch),
            new(Partition.DatabaseId, Projects, Capability.DocumentsRead),
            new(Partition.DatabaseId, Graph, Capability.GraphRead),
            new(Partition.DatabaseId, ExpansionGraph, Capability.GraphRead)
        ], grants.ToImmutable());
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        var keyId = "three-way-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        var credential = new ApiKeyRecord(keyId, principal.Id, verifier);
        await McpCallerAssertions.SdkStatusAsync(await admin.ConfigureApiKeyAsync(Guid.NewGuid(), credential, cancellationToken));
        return new(persisted, credential, secret);
    }

    internal static async Task RevokeAsync(ClusterFixture fixture, McpPersistedIdentity identity,
        CancellationToken cancellationToken)
    {
        var revoked = identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = identity.Principal.PolicyEpoch + 1
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), revoked,
            cancellationToken));
    }

    private static ResourceDefinition GraphResource() => new(Graph, ResourceKind.Graph, "three-way-domain")
    {
        FieldPolicies = [new("/label", "graph-label", RawUseGrant: LabelGrant)]
    };

    private static ResourceDefinition DocumentsResource() => new(Documents, ResourceKind.Collection, "three-way-domain")
    {
        FieldPolicies =
        [
            new(TextField, "text", RawUseGrant: TextGrant),
            new(VectorField, "vector", RawUseGrant: VectorGrant)
        ]
    };

    private static async Task ConfigureAsync(KeyLoadClient admin, PartitionRef partition,
        ResourceDefinition resource, CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), cancellationToken));

    private GraphWalkSpec Walk(string root, int depth)
        => new(Graph, [new(Partition, Projects, root)], depth, MaxVertices: 30, MaxEdges: 60);

}
