using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingRf3Scenario
{
    internal const string Graph = "incoming-links";
    internal const string Collection = "incoming-vertices";
    internal const string Tool = "keyload_graph_incoming";
    internal const string Projection = "eventual-reverse.v1";
    internal const string Label = "incoming-edge";
    internal const string SecretPath = "/secret";
    internal const string SecretGrant = "graph.incoming.secret.read";
    internal const string WriteGrant = "graph.incoming.secret.write";
    internal const string PrivateMarker = "incoming-private-marker";
    internal const string PublicMarker = "incoming-public-marker";
    internal const string EmptyJson = "{}";
    private const string Tenant = "graph-incoming-tenant";
    private const string Domain = "graph-incoming-domain";
    private const string TargetDatabase = "graph-incoming-target-db";
    private const string FirstDatabase = "graph-incoming-first-db";
    private const string SecondDatabase = "graph-incoming-second-db";
    private const string TargetId = "target";
    private const string LocalSourceId = "local-source";
    private const string SharedSourceId = "shared-source";
    private const string ReaderPrefix = "graph-incoming-reader-";
    private const string KeyPrefix = "graph-incoming-key-";
    private const string SecretSeparator = ".";
    private const string GuidFormat = "N";
    private const int RequestVersion = 1;
    private const int SecretBytes = 32;

    internal static async Task<GraphIncomingRf3Seed> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var scope = Guid.NewGuid().ToString(GuidFormat);
        var targetPartition = Partition(TargetDatabase + "-" + scope, scope);
        var firstSourcePartition = Partition(FirstDatabase + "-" + scope, scope);
        var secondSourcePartition = Partition(SecondDatabase + "-" + scope, scope);
        var target = Vertex(targetPartition, TargetId);
        var localSource = Vertex(targetPartition, LocalSourceId);
        var firstSource = Vertex(firstSourcePartition, SharedSourceId);
        var secondSource = Vertex(secondSourcePartition, SharedSourceId);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigurePartitionAsync(admin, targetPartition, cancellationToken).ConfigureAwait(false);
        await ConfigurePartitionAsync(admin, firstSourcePartition, cancellationToken).ConfigureAwait(false);
        await ConfigurePartitionAsync(admin, secondSourcePartition, cancellationToken).ConfigureAwait(false);
        await VerifySameOwnerAsync(fixture, [targetPartition, firstSourcePartition, secondSourcePartition],
            cancellationToken).ConfigureAwait(false);
        await SeedVerticesAsync(admin, targetPartition, [target, localSource], cancellationToken).ConfigureAwait(false);
        await SeedVerticesAsync(admin, firstSourcePartition, [firstSource], cancellationToken).ConfigureAwait(false);
        await SeedVerticesAsync(admin, secondSourcePartition, [secondSource], cancellationToken).ConfigureAwait(false);
        var reader = await CreateReaderAsync(admin, [targetPartition, firstSourcePartition,
            secondSourcePartition], cancellationToken).ConfigureAwait(false);
        var targetOnly = await CreateReaderAsync(admin, [targetPartition], cancellationToken).ConfigureAwait(false);
        return new(targetPartition, firstSourcePartition, secondSourcePartition, target,
            localSource, firstSource, secondSource, reader, targetOnly, Graph);
    }

    internal static ReadIncomingGraphEdgesRequestV1 Request(GraphIncomingRf3Seed seed, int limit = 10)
        => new(RequestVersion, seed.Target, seed.Graph, limit);

    internal static UpsertEdge Edge(string id, EntityRef from, EntityRef to, string label = Label,
        string? marker = null, long? expectedRevision = null)
        => new(Graph, id, from, to, label, Attributes(marker), expectedRevision);

    internal static Task<long> CommitAsync(KeyLoadClient client, PartitionRef partition,
        CancellationToken cancellationToken, params Mutation[] mutations)
        => CommitCoreAsync(client, partition, mutations, cancellationToken);

    internal static async Task DeliverAsync(KeyLoadClient administrator, PartitionRef sourcePartition,
        EntityRef destination, string edgeId, long revision, CancellationToken cancellationToken)
    {
        await ApplyAsync(administrator, sourcePartition, destination, edgeId, revision, cancellationToken)
            .ConfigureAwait(false);
        await CompleteAsync(administrator, sourcePartition, destination, edgeId, revision, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static Task ApplyAsync(KeyLoadClient administrator, PartitionRef sourcePartition,
        EntityRef destination, string edgeId, long revision, CancellationToken cancellationToken)
        => CommitAsync(administrator, destination.Partition, cancellationToken,
            new ApplyCrossPartitionReverseEdge(sourcePartition, Graph, edgeId, destination, revision));

    internal static Task CompleteAsync(KeyLoadClient administrator, PartitionRef sourcePartition,
        EntityRef destination, string edgeId, long revision, CancellationToken cancellationToken)
        => CommitAsync(administrator, sourcePartition, cancellationToken,
            new CompleteCrossPartitionReverseEdge(sourcePartition, Graph, edgeId, destination, revision));

    internal static Task<McpPersistedIdentity> CreateDocumentOnlyReaderAsync(KeyLoadClient admin,
        GraphIncomingRf3Seed seed, CancellationToken cancellationToken)
        => CreateIdentityAsync(admin, seed.TargetPartition.TenantId, [new ScopeGrant(seed.TargetPartition.DatabaseId, Collection,
            Capability.DocumentsRead)], cancellationToken);

    internal static string Attributes(string? marker)
        => marker is null ? EmptyJson : "{\"secret\":\"" + PrivateMarker + "\",\"public\":\""
            + marker + "\"}";

    private static PartitionRef Partition(string database, string scope)
        => new(Tenant + "-" + scope, database, Domain + "-" + scope, Guid.NewGuid().ToString(GuidFormat));

    private static EntityRef Vertex(PartitionRef partition, string id)
        => new(partition, Collection, id);

    private static async Task ConfigurePartitionAsync(KeyLoadClient admin, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var collection = new ResourceDefinition(Collection, ResourceKind.Collection,
            partition.TransactionDomainId);
        var graph = new ResourceDefinition(Graph, ResourceKind.Graph, partition.TransactionDomainId)
        {
            FieldPolicies = [new(SecretPath, "graph-incoming-private", RawReadGrant: SecretGrant,
                WriteGrant: WriteGrant)]
        };
        foreach (var resource in new[] { collection, graph })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, resource), cancellationToken)).ConfigureAwait(false);
        }
    }

    private static async Task VerifySameOwnerAsync(ClusterFixture fixture, PartitionRef[] partitions,
        CancellationToken cancellationToken)
    {
        var expected = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App,
            McpCallerProtocol.Node1, fixture.AdminKey, new(1, partitions[0]), cancellationToken)
            .ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.ValidOwnerAsync(expected, fixture.PhysicalShardId)
            .ConfigureAwait(false);
        foreach (var partition in partitions.Skip(1))
        {
            var actual = await AtomicPartitionPlacementRf3Assertions.SdkReadAsync(fixture.App,
                McpCallerProtocol.Node1, fixture.AdminKey, new(1, partition), cancellationToken)
                .ConfigureAwait(false);
            await AtomicPartitionPlacementRf3Assertions.SameOwnerAsync(expected, actual).ConfigureAwait(false);
        }
    }

    private static async Task SeedVerticesAsync(KeyLoadClient admin, PartitionRef partition,
        EntityRef[] vertices, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [.. vertices.Select(vertex => (Mutation)new PutDocument(vertex.Collection, vertex.Id, EmptyJson))]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(command, cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task<McpPersistedIdentity> CreateReaderAsync(KeyLoadClient admin,
        PartitionRef[] partitions, CancellationToken cancellationToken)
    {
        var grants = partitions.SelectMany(partition => new[]
        {
            new ScopeGrant(partition.DatabaseId, Collection, Capability.DocumentsRead),
            new ScopeGrant(partition.DatabaseId, Graph, Capability.GraphRead)
        }).ToImmutableArray();
        return await CreateIdentityAsync(admin, partitions[0].TenantId, grants, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<McpPersistedIdentity> CreateIdentityAsync(KeyLoadClient admin, string tenant,
        ImmutableArray<ScopeGrant> grants, CancellationToken cancellationToken)
    {
        var principalId = ReaderPrefix + Guid.NewGuid().ToString(GuidFormat);
        var principal = new PrincipalRecord(principalId, tenant, grants, []);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), principal,
            cancellationToken)).ConfigureAwait(false);
        var keyId = KeyPrefix + Guid.NewGuid().ToString(GuidFormat);
        var secret = keyId + SecretSeparator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var apiKey = new ApiKeyRecord(keyId, principalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureApiKeyAsync(Guid.NewGuid(), apiKey,
            cancellationToken)).ConfigureAwait(false);
        return new(principal, apiKey, secret);
    }

    private static async Task<long> CommitCoreAsync(KeyLoadClient client, PartitionRef partition,
        Mutation[] mutations, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, cancellationToken))
            .ConfigureAwait(false);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(mutations.Length);
        return receipt.Mutations[^1].Revision;
    }
}
