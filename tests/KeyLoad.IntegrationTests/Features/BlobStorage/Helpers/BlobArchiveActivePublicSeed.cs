using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicSeed
{
    internal static Task<BlobArchiveActivePublicState> CreateAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
        => ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async (sdk, official) =>
            {
                var blob = new BlobRef(seed.Partition, RelationalSqlRf3Tokens.Blobs, "archive-published-multipart");
                var published = await BlobArchiveActivePublicPublication.CreateAsync(sdk, official, blob, 3, true, 0, token);
                var active = await BlobArchiveActivePublicPublication.CreateAsync(sdk, official,
                    blob with { Id = "archive-abandoned-active" }, 2, false, 0, token);
                var id = "archive-reader-" + Guid.NewGuid().ToString("N");
                var key = id + "." + Guid.NewGuid().ToString("N");
                var principal = new PrincipalRecord(id, seed.Partition.TenantId,
                    [new(seed.Partition.DatabaseId, blob.Resource, Capability.BlobRead)], []);
                await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                    await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
                await Assert.That((await McpCallerAssertions.SdkStatusAsync(await sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
                    DatabaseEngine.Credential(id, id, key), token))).IsSuccess).IsTrue();
                var state = new BlobArchiveActivePublicState(published, active, principal, key, seed.OriginalPlacement.PhysicalShardId);
                await BlobArchiveActivePublicAssertions.CurrentAsync(sdk, official, published, token);
                await BlobArchiveActivePublicAssertions.ActiveAsync(sdk, official, active, false, token);
                return state;
            }, token);
}
