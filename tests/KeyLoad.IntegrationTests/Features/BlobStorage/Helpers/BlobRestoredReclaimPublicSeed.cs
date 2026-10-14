using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRestoredReclaimPublicSeed
{
    private const string Resource = "restored-reclaim-capacity";
    private const string Published = "published-multipart";
    private const string Active = "abandoned-active";
    internal static Task<BlobRestoredReclaimPublicState> CreateAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
        => ClusterRestoreRf3CallerOwner.RunAsync(source.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async (sdk, official) =>
            {
                var definition = new ResourceDefinition(Resource, ResourceKind.BlobStore, seed.Partition.TransactionDomainId)
                {
                    BlobPolicy = new() { MaxBlobBytes = 196625, MaxReservedBytes = 327714 }
                };
                var request = new ConfigureResourceRequest(seed.Partition.TenantId, seed.Partition.DatabaseId, definition);
                var id = Guid.NewGuid();
                await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(id, request, token)));
                var args = McpOfficialClient.Arguments(request);
                args.Add(McpCallerProtocol.CommandId, id);
                await SqlRf3Protocol.EqualAsync(definition, (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(
                    await official.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure, args, cancellationToken: token))).Value);
                var published = await BlobArchiveActivePublicPublication.CreateAsync(sdk, official,
                    new(seed.Partition, Resource, Published), 3, true, 0, token);
                var active = await BlobArchiveActivePublicPublication.CreateAsync(sdk, official,
                    new(seed.Partition, Resource, Active), 2, false, 0, token);
                var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(new(1, seed.Partition), token));
                return new BlobRestoredReclaimPublicState(published, active, placement.PhysicalShardId);
            }, token);
}
