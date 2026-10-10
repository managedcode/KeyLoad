using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextCapturedRf3Publisher
{
    internal static async Task<McpPersistedIdentity> CreateAsync(KeyLoadClient administrator,
        PartitionRef partition, CancellationToken token)
    {
        var id = "c1-probe-" + Guid.NewGuid().ToString("N");
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, NativeTextMaintenanceRf3Scenario.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        { ClusterAdministrator = true };
        var key = id + "-online-text";
        var secret = key + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var credential = new ApiKeyRecord(key, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(JsonDefaults.Serialize(stored).SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        await Assert.That(stored.ClusterAdministrator).IsTrue();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token))).IsTrue();
        return new(stored, credential, secret);
    }
}
