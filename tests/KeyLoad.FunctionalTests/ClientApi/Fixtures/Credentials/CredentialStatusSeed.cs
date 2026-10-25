using System.Security.Cryptography;
using System.Text;
using KeyLoad.FunctionalTests.OrleansExecution;
using P = KeyLoad.FunctionalTests.ClientApi.CredentialStatusProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class CredentialStatusSeed
{
    internal static async Task<CredentialStatusState> CreateAsync(ConnectionScenario scenario)
    {
        var database = scenario.Fixture.Database;
        var ordinary = scenario.Principal();
        var administrator = new PrincipalRecord(P.AdminPrefix + Guid.NewGuid().ToString(P.GuidFormat),
            database.Partition.TenantId, [], [])
        {
            ClusterAdministrator = true
        };
        await CredentialStatusAssertions.EqualAsync(administrator, database.Submit(
            OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(administrator)).Get<PrincipalRecord>());
        var caller = Credential(P.CallerKeyId, administrator.Id, P.CallerKey);
        await Assert.That(database.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(caller)).Get<bool>()).IsTrue();
        var state = new CredentialStatusState(scenario, administrator, ordinary, caller,
            Credential(P.OriginalKeyId, ordinary.Id, P.OriginalKey),
            Credential(P.DeniedKeyId, ordinary.Id, P.DeniedKey), Credential(P.HealthyKeyId, ordinary.Id, P.HealthyKey));
        await Assert.That(state.OriginalId).IsNotEqualTo(state.HealthyId);
        await Assert.That(state.OriginalId).IsNotEqualTo(state.DeniedId);
        await Assert.That(state.HealthyId).IsNotEqualTo(state.DeniedId);
        await CredentialStatusAssertions.CurrentAsync(state, original: false, healthy: false);
        return state;
    }

    private static ApiKeyRecord Credential(string id, string principal, string secret)
        => new(id, principal, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
}
