using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>A real persisted scoped principal and verifier; the secret remains solely in actual caller headers.</summary>
internal sealed record IsolatedKeyLoadPublicChecksIdentity(string Secret)
{
    internal static async Task<IsolatedKeyLoadPublicChecksIdentity> CreateAsync(KeyLoadClient administrator,
        PartitionRef partition, string resource, Capability capability, CancellationToken token)
    {
        var principal = new PrincipalRecord("isolated-principal-" + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, resource, capability)], []);
        var keyId = "isolated-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var key = new ApiKeyRecord(keyId, principal.Id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var persisted = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(
            await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(persisted.Id).IsEqualTo(principal.Id);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(principal.Grants, persisted.Grants);
        var configured = await administrator.ConfigureApiKeyAsync(Guid.NewGuid(), key, token);
        await Assert.That(configured.IsSuccess).IsTrue();
        await Assert.That(configured.Problem).IsNull();
        return new(secret);
    }
}
