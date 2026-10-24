using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Search;

internal sealed record FilteredSearchClusterScenario(PartitionRef Partition)
{
    internal const string Collection = "filtered-cluster-records";
    internal const string TextField = "/text";
    internal const string VectorField = "/embedding";
    internal const string TextGrant = "filtered-cluster.text.use";
    internal const string VectorGrant = "filtered-cluster.vector.use";
    internal const string OwnerA = "filtered-owner-a";
    internal const string OwnerB = "filtered-owner-b";
    internal const int FusionConstant = 60;
    internal const int FirstRevision = 1;
    internal const int SecondRevision = 2;
    internal static VectorSpace Space
    {
        get;
    } = new("filtered-cluster-space", 2,
        DistanceMetric.Cosine, "filtered-cluster-model", "1");

    internal static async Task<FilteredSearchClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("filtered-cluster-tenant-" + Guid.NewGuid().ToString("N"),
            "filtered-cluster-database", "filtered-cluster-domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies =
            [
                new(TextField, "filtered-text", RawUseGrant: TextGrant),
                new(VectorField, "filtered-vector", RawUseGrant: VectorGrant)
            ]
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(Seed(partition), cancellationToken));
        return new(partition);
    }

    internal SearchRequest Text(string text, ImmutableArray<string>? allowedIds = null)
        => new(Partition, Collection, TextField, text, Limit: 10, FusionConstant: FusionConstant,
            AllowedIds: allowedIds);

    internal SearchRequest Vector(ImmutableArray<string>? allowedIds = null)
        => new(Partition, Collection, VectorField: VectorField, Vector: [1, 0], Space: Space,
            Limit: 10, FusionConstant: FusionConstant, AllowedIds: allowedIds);

    internal SearchRequest Hybrid(string text, ImmutableArray<string>? allowedIds = null)
        => new(Partition, Collection, TextField, text, VectorField, [1, 0], Space, 10,
            FusionConstant: FusionConstant, AllowedIds: allowedIds);

    internal async Task<McpPersistedIdentity> CreateReaderAsync(ClusterFixture fixture,
        bool textGrant, bool vectorGrant, bool restrictRows, string? owner,
        CancellationToken cancellationToken)
    {
        var grants = ImmutableArray.CreateBuilder<string>();
        if (textGrant)
        {
            grants.Add(TextGrant);
        }
        if (vectorGrant)
        {
            grants.Add(VectorGrant);
        }
        var principal = new PrincipalRecord("filtered-cluster-principal-" + Guid.NewGuid().ToString("N"),
            Partition.TenantId,
            [new(Partition.DatabaseId, Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], grants.ToImmutable())
        {
            RestrictRows = restrictRows,
            OwnerId = owner
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        var keyId = "filtered-cluster-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkStatusAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(keyId, principal.Id, verifier), cancellationToken));
        return new(principal, new(keyId, principal.Id, verifier), secret);
    }

    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, "a", "{\"text\":\"alpha alpha\"}",
                Access: new(OwnerA)),
            new PutDocument(Collection, "b", "{\"text\":\"alpha beta\"}",
                Access: new(OwnerB)),
            new PutDocument(Collection, "c", "{\"text\":\"beta\"}",
                Access: new(OwnerB)),
            new PutDocument(Collection, "d", "{\"text\":\"gamma\"}",
                Access: new(OwnerB)),
            new PutVector(Collection, "a", VectorField, [1, 0], Space, FirstRevision),
            new PutVector(Collection, "b", VectorField, [0, 1], Space, FirstRevision),
            new PutVector(Collection, "c", VectorField, [-1, 0], Space, FirstRevision),
            new PutVector(Collection, "d", VectorField, [0, 1], Space, FirstRevision)
        ]);
}
