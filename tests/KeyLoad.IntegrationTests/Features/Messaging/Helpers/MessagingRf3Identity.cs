using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Owns one actually persisted scoped principal and API credential for public RF3 callers.</summary>
internal sealed record MessagingRf3Identity(PrincipalRecord Principal, ApiKeyRecord Credential, string Secret)
{
    private const int SecretBytes = 32;
    private const string PrincipalPrefix = "messaging-principal-";
    private const string CredentialPrefix = "messaging-credential-";
    private const string SecretSeparator = ".";

    internal static async Task<MessagingRf3Identity> CreateAsync(ClusterFixture fixture,
        string tenantId, IReadOnlyCollection<ScopeGrant> grants, ImmutableArray<string> fieldGrants,
        bool clusterAdministrator, CancellationToken cancellationToken)
    {
        var principalId = PrincipalPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var principal = new PrincipalRecord(principalId, tenantId,
            [.. grants], fieldGrants)
        { ClusterAdministrator = clusterAdministrator };
        var credentialId = CredentialPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var secret = credentialId + SecretSeparator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(credentialId, principalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal,
            cancellationToken));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), credential, cancellationToken))).IsTrue();
        return new(principal, credential, secret);
    }

    internal static async Task UpdateAsync(ClusterFixture fixture, PrincipalRecord updated,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), updated,
            cancellationToken));
    }

    internal static PrincipalRecord WithCapability(PrincipalRecord current, QueueLaneRef lane, Capability capabilities)
        => current with
        {
            Grants = [.. current.Grants.Select(grant => grant.Database == lane.Partition.DatabaseId
                && grant.Resource == lane.Queue ? grant with { Capabilities = capabilities } : grant)],
            PolicyEpoch = checked(current.PolicyEpoch + 1)
        };

    internal static PrincipalRecord WithFieldGrants(PrincipalRecord current, ImmutableArray<string> fieldGrants)
        => current with { FieldGrants = fieldGrants, PolicyEpoch = checked(current.PolicyEpoch + 1) };
}
