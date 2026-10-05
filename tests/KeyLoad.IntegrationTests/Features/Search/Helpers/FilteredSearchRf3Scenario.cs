using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record FilteredSearchRf3Scenario(PartitionRef Partition)
{
    internal const string Collection = "filtered-rf3-records";
    internal const string TextField = "/text";
    internal const string VectorField = "/embedding";
    internal const string TextGrant = "filtered-rf3.text.use";
    internal const string VectorGrant = "filtered-rf3.vector.use";
    internal const string OwnerA = "filtered-owner-a";
    internal const string OwnerB = "filtered-owner-b";
    internal const int FusionConstant = 60;
    internal const int FirstRevision = 1;
    internal const int SecondRevision = 2;
    internal static VectorSpace Space { get; } = new("filtered-rf3-space", 2,
        DistanceMetric.Cosine, "filtered-rf3-model", "1");

    internal static async Task<FilteredSearchRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("filtered-rf3-tenant-" + Guid.NewGuid().ToString("N"),
            "filtered-rf3-database", "filtered-rf3-domain", Guid.NewGuid().ToString("N"));
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
        { grants.Add(TextGrant); }
        if (vectorGrant)
        { grants.Add(VectorGrant); }
        var principal = new PrincipalRecord("filtered-rf3-principal-" + Guid.NewGuid().ToString("N"),
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
        var keyId = "filtered-rf3-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
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
