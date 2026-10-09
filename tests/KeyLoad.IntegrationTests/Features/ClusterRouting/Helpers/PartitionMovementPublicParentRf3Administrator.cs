using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>One actual persisted administrator matches the existing closed diagnostic principal namespace.</summary>
internal static class PartitionMovementPublicParentRf3Administrator
{
    internal const string PrincipalId = "c1-probe-public-parent-admin";
    private const string Tenant = "protected-parent-tenant";
    private const string CredentialPrefix = "parent-admin-key-";
    private const string CredentialSeparator = ".";
    private const int SecretBytes = 32;
    private const long InitialPolicyEpoch = 1;

    internal static async Task<string> PersistAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        var key = CredentialPrefix + Guid.NewGuid().ToString("N");
        var secret = key + CredentialSeparator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var principal = new PrincipalRecord(PrincipalId, Tenant, [], [])
        { ClusterAdministrator = true, PolicyEpoch = InitialPolicyEpoch };
        var credential = new ApiKeyRecord(key, PrincipalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        using var sourceHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        using var targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var source = new KeyLoadClient(sourceHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        var target = new KeyLoadClient(targetHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureAsync(source, principal, credential, cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(target, principal, credential, cancellationToken).ConfigureAwait(false);
        return secret;
    }

    private static async Task ConfigureAsync(KeyLoadClient root, PrincipalRecord principal,
        ApiKeyRecord credential, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken).ConfigureAwait(false));
        await Assert.That(actual.Id).IsEqualTo(principal.Id);
        await Assert.That(actual.ClusterAdministrator).IsTrue();
        await Assert.That(actual.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(),
            credential, cancellationToken).ConfigureAwait(false))).IsTrue();
    }
}
