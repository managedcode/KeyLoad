using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>A genuinely configured database principal and credential used by independent official callers.</summary>
/// <param name="Principal">The actual persisted identity and grants.</param>
/// <param name="Credential">The actual persisted SHA-256 credential record.</param>
/// <param name="Secret">The private generated credential sent in the real bearer header.</param>
internal sealed record McpPersistedIdentity(PrincipalRecord Principal, ApiKeyRecord Credential, string Secret)
{
    private const int SecretBytes = 32;
    private const string PrincipalPrefix = "mcp-principal-";
    private const string CredentialPrefix = "mcp-credential-";
    private const string Separator = ".";

    /// <summary>Creates the actual scoped principal and key through the real HTTP SDK.</summary>
    /// <param name="fixture">The actual Orleans application.</param>
    /// <param name="partition">The sole tenant, database and resource domain allowed to this identity.</param>
    /// <param name="capabilities">Persisted server-granted operations.</param>
    /// <param name="cancellationToken">The bounded setup lifetime.</param>
    /// <returns>The genuinely persisted identity and its private generated secret.</returns>
    internal static Task<McpPersistedIdentity> CreateAsync(ClusterFixture fixture, PartitionRef partition,
        Capability capabilities, CancellationToken cancellationToken)
        => CreateAsync(fixture, partition, McpDocumentProtocol.Collection, capabilities, cancellationToken);

    /// <summary>Creates a persisted principal and key scoped to the requested real database resource.</summary>
    /// <param name="fixture">The actual Orleans application.</param>
    /// <param name="partition">The tenant, database and atomic partition used by the scenario.</param>
    /// <param name="resourceName">The sole resource receiving the persisted grants.</param>
    /// <param name="capabilities">Persisted server-granted operations.</param>
    /// <param name="cancellationToken">The bounded setup lifetime.</param>
    /// <returns>The genuinely persisted identity and its private generated secret.</returns>
    internal static async Task<McpPersistedIdentity> CreateAsync(ClusterFixture fixture, PartitionRef partition,
        string resourceName, Capability capabilities, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        return await CreateAsync(administrator, partition, resourceName, capabilities, cancellationToken);
    }

    /// <summary>Reuses execution persisted identity creation with an already owned real administrator SDK.</summary>
    /// <param name="administrator">The real public SDK authenticated as the persisted administrator.</param>
    /// <param name="partition">The actual identity tenant and scoped database.</param>
    /// <param name="resourceName">The resource receiving the initial persisted grant.</param>
    /// <param name="capabilities">The initial execution capability set.</param>
    /// <param name="cancellationToken">The original bounded caller lifetime.</param>
    /// <returns>The genuinely persisted identity and its private generated credential.</returns>
    internal static async Task<McpPersistedIdentity> CreateAsync(KeyLoadClient administrator, PartitionRef partition,
        string resourceName, Capability capabilities, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        var id = PrincipalPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, resourceName, capabilities)], []);
        var keyId = CredentialPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var secret = keyId + Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(keyId, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal, cancellationToken));
        await Assert.That(persisted.Id).IsEqualTo(principal.Id);
        await Assert.That((await McpCallerAssertions.SdkStatusAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(), credential, cancellationToken))).IsSuccess).IsTrue();
        return new(principal, credential, secret);
    }
}
