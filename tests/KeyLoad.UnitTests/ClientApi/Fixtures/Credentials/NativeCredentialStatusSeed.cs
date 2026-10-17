using System.Security.Cryptography;
using System.Text;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusSeed
{
    internal static async Task<NativeCredentialStatusState> CreateAsync(ConnectionNativeScenario scenario)
    {
        var database = scenario.Fixture.Database;
        var ordinary = scenario.Principal();
        var administrator = new PrincipalRecord(P.AdminPrefix + Guid.NewGuid().ToString(P.GuidFormat),
            database.Partition.TenantId, [], [])
        {
            ClusterAdministrator = true
        };
        await NativeCredentialStatusAssertions.EqualAsync(administrator, database.Submit(
            OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(administrator)).Get<PrincipalRecord>());
        var caller = Credential(P.CallerKeyId, administrator.Id, P.CallerKey);
        await Assert.That(database.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(caller)).Get<bool>()).IsTrue();
        var state = new NativeCredentialStatusState(scenario, administrator, ordinary, caller,
            Credential(P.OriginalKeyId, ordinary.Id, P.OriginalKey),
            Credential(P.DeniedKeyId, ordinary.Id, P.DeniedKey), Credential(P.HealthyKeyId, ordinary.Id, P.HealthyKey));
        await Assert.That(state.OriginalId).IsNotEqualTo(state.HealthyId);
        await Assert.That(state.OriginalId).IsNotEqualTo(state.DeniedId);
        await Assert.That(state.HealthyId).IsNotEqualTo(state.DeniedId);
        await NativeCredentialStatusAssertions.CurrentAsync(state, original: false, healthy: false);
        return state;
    }

    private static ApiKeyRecord Credential(string id, string principal, string secret)
        => new(id, principal, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
}
