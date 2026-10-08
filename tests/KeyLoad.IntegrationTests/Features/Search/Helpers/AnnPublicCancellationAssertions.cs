using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnPublicCancellationAssertions
{
    private const string PrincipalPrefix = "c1-probe-";
    private const string CredentialSuffix = "-ann-read";
    private const string Separator = ".";
    private const string GuidFormat = "N";
    private const int SecretBytes = 32;
    private const Capability ReadCapabilities = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;

    internal static async Task<McpPersistedIdentity> IdentityAsync(KeyLoadClient administrator,
        PartitionRef partition, CancellationToken token)
    {
        var id = PrincipalPrefix + Guid.NewGuid().ToString(GuidFormat);
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, NativeAnnMaintenanceRf3Scenario.Collection, ReadCapabilities)], []);
        var key = id + CredentialSuffix;
        var secret = key + Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(key, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(stored.Id).IsEqualTo(id);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
        await Assert.That(stored.ClusterAdministrator).IsFalse();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token))).IsTrue();
        return new(stored, credential, secret);
    }

    internal static async Task<ReplicaSiloDiscovery[]> DiscoveryAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken token)
    {
        var observations = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < observations.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            observations[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app,
                RequestCqrsRf3Protocol.NodeName(index), profile, token);
        }
        return observations;
    }

    internal static async Task<RequestCqrsFaultMcpObservation> CallMcpAsync(McpOfficialClient client,
        ApproximateSearchRequest request, CancellationToken token)
    {
        try
        {
            return new(await client.CallAsync(AnnSearchProtocol.Tool, request, token), null);
        }
        catch (OperationCanceledException error) { return new(null, error); }
        catch (HttpRequestException error) { return new(null, error); }
        catch (IOException error) { return new(null, error); }
    }
}
