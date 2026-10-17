using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Seed
{
    private const int SecretBytes = 32;
    private const string TenantPrefix = "chunk-minimum-";
    private const string CredentialPrefix = "chunk-minimum-key-";
    internal const string TargetSeries = "minimum-target-series";
    internal const string TargetId = "minimum-target-record";
    internal const double TargetValue = 11d;
    internal const long TargetTicks = 2;

    internal static async Task<ChunkMinimumRf3State> RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var administrator = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token);
        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            administrator, source => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                TwoRf3MembershipProtocol.Node4, administrator, async target =>
                {
                    var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString("N"),
                        TimeSeriesRf3Scenario.Database, TimeSeriesRf3Scenario.TransactionDomain, Guid.NewGuid().ToString("N"));
                    var definition = new ResourceDefinition(TimeSeriesRf3Scenario.Set, ResourceKind.TimeSeries,
                        partition.TransactionDomainId)
                    {
                        FieldPolicies = [new(TimeSeriesRf3Scenario.SecretField, TimeSeriesRf3Scenario.SecretClassification,
                            TimeSeriesRf3Scenario.SecretGrant, TimeSeriesRf3Scenario.SecretGrant)]
                    };
                    foreach (var root in new[] { source, target })
                    {
                        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId,
                                partition.DatabaseId, definition), token)));
                    }
                    var reader = await IdentityAsync(source, target, partition, token);
                    var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
                    var owner = await McpCallerAssertions.SdkSuccessAsync(
                        await source.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
                    var originals = new List<ChunkMinimumRf3Original>();
                    var window = await ChunkMinimumRf3Writes.WindowAsync(source, partition, owner,
                        TimeSeriesRf3Scenario.Series, TimeSeriesResourceTagPolicyRf3Assertions.Expected(
                            TimeSeriesRf3Scenario.PrivateTags), TwoRf3MembershipProtocol.Node1, originals, token);
                    var outbox = await McpCallerAssertions.SdkSuccessAsync(await source.Sdk.OutboxStatusAsync(partition, token));
                    return new ChunkMinimumRf3State(partition, administrator, reader, directory, owner, [window], originals, outbox);
                }, token), token);
    }

    private static async Task<McpPersistedIdentity> IdentityAsync(RequestCqrsRf3Callers source,
        RequestCqrsRf3Callers target, PartitionRef partition, CancellationToken token)
    {
        var principal = new PrincipalRecord("minimum-reader-" + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, TimeSeriesRf3Scenario.Set, Capability.SeriesRead)], [TimeSeriesRf3Scenario.SecretGrant]);
        var id = CredentialPrefix + Guid.NewGuid().ToString("N");
        var secret = id + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var key = new ApiKeyRecord(id, principal.Id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        foreach (var root in new[] { source, target })
        {
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
            await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), key, token));
        }
        await Assert.That(principal.ClusterAdministrator).IsFalse();
        return new(principal, key, secret);
    }
}
