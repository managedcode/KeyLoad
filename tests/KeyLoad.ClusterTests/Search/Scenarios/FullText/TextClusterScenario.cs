using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.Search;

/// <summary>Persists the canonical three-document text/vector corpus through the actual SDK.</summary>
internal sealed record TextClusterScenario(PartitionRef Partition, ResourceDefinition Resource, CommitToken SeedToken,
    EntityRefLifetime Lifetime)
{
    internal const string Collection = "execution-text";
    internal const string TextField = "/text";
    internal const string VectorField = "/embedding";
    internal const string SecretField = "/secret";
    internal const string TextUseGrant = "execution-text.text.use";
    internal const string VectorUseGrant = "execution-text.vector.use";
    internal const string SecretReadGrant = "execution-text.secret.read";
    internal const string Secret = "private-search-canary";
    internal const string FirstId = "a";
    internal const string SecondId = "b";
    internal const string ThirdId = "c";
    internal const string OwnerA = "owner-a";
    internal const string OwnerB = "owner-b";
    internal const string FailureScenario = "execution-text-silo-restart";
    internal const int FirstRevision = 1;
    internal const int UpdatedRevision = 2;
    internal const int MaxFusionConstant = 60;
    internal const int CorpusCount = 3;
    internal const int VectorDimension = 2;
    internal const string ProviderModel = "execution-text-test-model";
    internal const string ProviderVersion = "1";

    internal static async Task<TextClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("execution-text-tenant-" + Guid.NewGuid().ToString("N"),
            "execution-text-database", "execution-text-domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies =
            [
                new("/text", "search-text", RawUseGrant: TextUseGrant),
                new("/embedding", "search-vector", RawUseGrant: VectorUseGrant),
                new(SecretField, "private", RawReadGrant: SecretReadGrant)
            ]
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
        var persistedResource = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(creationId,
            creation, cancellationToken));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(SeedCommand(partition), cancellationToken));
        return new(partition, persistedResource, receipt.Token, ResourceCreationLifetime.Create(creationId, creation));
    }

    internal static CommandRequest SeedCommand(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, FirstId, Document("Київ needle needle", "owner-a"), ExpectedRevision: null,
                Access: new(OwnerA)),
            new PutDocument(Collection, SecondId, Document("needle plain", "owner-b"), ExpectedRevision: null,
                Access: new(OwnerB)),
            new PutDocument(Collection, ThirdId, Document("other", "owner-b"), ExpectedRevision: null,
                Access: new(OwnerB)),
            new PutVector(Collection, FirstId, VectorField, [1, 0], SpaceFor(), FirstRevision),
            new PutVector(Collection, SecondId, VectorField, [0.8f, 0.6f], SpaceFor(), FirstRevision),
            new PutVector(Collection, ThirdId, VectorField, [0, 1], SpaceFor(), FirstRevision)
        ]);

    internal EntityRef StoredReference(string id) => new(Partition, Collection, id) { Lifetime = Lifetime };

    internal SearchRequest Hybrid(string text = "needle", int limit = CorpusCount)
        => new(Partition, Collection, TextField, text, VectorField, [1, 0], SpaceFor(), limit,
            FusionConstant: MaxFusionConstant);

    internal SearchRequest Text(string text, int limit = CorpusCount)
        => new(Partition, Collection, TextField, text, Limit: limit, FusionConstant: MaxFusionConstant);

    internal async Task<McpPersistedIdentity> CreateReaderAsync(ClusterFixture fixture,
        bool includeTextUse, bool includeVectorUse, bool includeSecretRead, bool restrictRows,
        CancellationToken cancellationToken)
    {
        var grants = ImmutableArray.CreateBuilder<string>();
        if (includeTextUse)
        {
            grants.Add(TextUseGrant);
        }
        if (includeVectorUse)
        {
            grants.Add(VectorUseGrant);
        }
        if (includeSecretRead)
        {
            grants.Add(SecretReadGrant);
        }
        var principal = new PrincipalRecord("execution-text-principal-" + Guid.NewGuid().ToString("N"),
            Partition.TenantId, [new(Partition.DatabaseId, Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], grants.ToImmutable())
        {
            RestrictRows = restrictRows,
            OwnerId = OwnerA
        };
        return await TextClusterScenario.ConfigureIdentityAsync(fixture, principal, cancellationToken);
    }

    internal static async Task<McpPersistedIdentity> ConfigureIdentityAsync(ClusterFixture fixture,
        PrincipalRecord principal, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        var keyId = "execution-text-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkStatusAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(keyId, principal.Id, verifier), cancellationToken));
        return new(persisted, new(keyId, principal.Id, verifier), secret);
    }

    internal static string Document(string text, string owner)
        => "{\"text\":\"" + text + "\",\"embedding\":\"unit\",\"secret\":\""
            + Secret + "\",\"owner\":\"" + owner + "\"}";

    internal static VectorSpace SpaceFor()
        => new("execution-text-space", VectorDimension, DistanceMetric.Cosine, ProviderModel, ProviderVersion);
}
