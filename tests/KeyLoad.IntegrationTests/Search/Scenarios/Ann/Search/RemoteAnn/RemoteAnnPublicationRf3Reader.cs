using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record RemoteAnnPublicationRf3Reader(PrincipalRecord Principal, ApiKeyRecord Credential,
    string Secret)
{
    internal static async Task<RemoteAnnPublicationRf3Reader> CreateAsync(TwoRf3MembershipWave wave,
        NativeAnnMaintenanceRf3Scenario scenario, string actualAdministrator, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(RemoteAnnPublicationRf3Protocol.IdentityFormat);
        var id = RemoteAnnPublicationRf3Protocol.ReaderPrefix + suffix;
        var key = RemoteAnnPublicationRf3Protocol.CredentialPrefix + suffix;
        var secret = key + RemoteAnnPublicationRf3Protocol.CredentialSeparator
            + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RemoteAnnPublicationRf3Protocol.SecretBytes));
        var principal = new PrincipalRecord(id, scenario.Partition.TenantId,
            [new(scenario.Partition.DatabaseId, NativeAnnMaintenanceRf3Scenario.Collection, Capability.VectorSearch)],
            [RemoteAnnPublicationRf3Protocol.RawRead, RemoteAnnPublicationRf3Protocol.RawUse]);
        var credential = new ApiKeyRecord(key, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var result = new RemoteAnnPublicationRf3Reader(principal, credential, secret);
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            using var http = McpCallerHttp.Create(wave.Application, node);
            var sdk = new KeyLoadClient(http, actualAdministrator, IntegrationClientOptions.Execution());
            var persisted = await McpCallerAssertions.SdkStatusAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
            await Assert.That(persisted.IsSuccess).IsTrue();
            var configured = await McpCallerAssertions.SdkStatusAsync(await sdk.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token));
            await Assert.That(configured.IsSuccess).IsTrue();
        }
        return result;
    }

    internal PrincipalRecord Denied() => Principal with
    {
        Grants = [],
        PolicyEpoch = RemoteAnnPublicationRf3Protocol.DeniedEpoch
    };

    internal PrincipalRecord FieldDenied() => Principal with
    {
        FieldGrants = [],
        PolicyEpoch = RemoteAnnPublicationRf3Protocol.FieldDeniedEpoch
    };

    internal PrincipalRecord FieldRepaired() => Principal with
    {
        PolicyEpoch = RemoteAnnPublicationRf3Protocol.FieldRepairedEpoch
    };

    internal PrincipalRecord Repaired() => Principal with
    {
        PolicyEpoch = RemoteAnnPublicationRf3Protocol.RepairedEpoch
    };
}
