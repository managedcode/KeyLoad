using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicPolicy
{
    internal static Task RunAsync(ClusterRestoreRf3Fixture target, PartitionMovementPublicParentRf3Seed seed,
        BlobArchiveActivePublicState state, CancellationToken token)
        => ClusterRestoreRf3CallerOwner.RunAsync(target.Application, BlobArchiveActivePublicCold.Node(target, state),
            seed.Credential, async (sdk, official) =>
            {
                await BlobArchiveActivePublicFences.RequireAsync(sdk, official, state, token);
                await ReaderAsync(target, state, token);
                var denied = state.Reader with
                {
                    Grants = [],
                    PolicyEpoch = 2L
                };
                await SqlRf3Protocol.EqualAsync(denied, await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
                await DeniedAsync(target, state, sdk, official, token);
                var repaired = state.Reader with
                {
                    PolicyEpoch = 3L
                };
                await SqlRf3Protocol.EqualAsync(repaired, await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
                state.Reader = repaired;
                await ReaderAsync(target, state, token);
                state.Published = await BlobArchiveActivePublicPublication.CreateAsync(sdk, official, state.Published.Begin.Blob, 3, true, 1, token);
                await Assert.That(state.Published.Completed!.Receipt.Token.Incarnation).IsEqualTo(target.Mappings.Single(mapping => mapping.Source.PhysicalShardId == state.SourceOwner).Target.Incarnation);
                await Assert.That(state.Published.Completed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
                await BlobArchiveActivePublicFences.ReplayAsync(sdk, official, state, token);
                await ReaderAsync(target, state, token);
                return true;
            }, token);

    internal static Task ReaderAsync(ClusterRestoreRf3Fixture target, BlobArchiveActivePublicState state, CancellationToken token)
        => ClusterRestoreRf3CallerOwner.RunAsync(target.Application, BlobArchiveActivePublicCold.Node(target, state), state.Key,
            async (sdk, official) =>
            {
                await BlobArchiveActivePublicAssertions.CurrentAsync(sdk, official, state.Published, token);
                return true;
            }, token);

    private static Task<bool> DeniedAsync(ClusterRestoreRf3Fixture target, BlobArchiveActivePublicState state,
        KeyLoad.Client.KeyLoadClient administrator, McpOfficialClient adminMcp, CancellationToken token)
        => ClusterRestoreRf3CallerOwner.RunAsync(target.Application, BlobArchiveActivePublicCold.Node(target, state), state.Key,
            async (sdk, official) =>
            {
                var metadata = new BlobMetadataRequest(state.Published.Begin.Blob);
                await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.GetBlobMetadataAsync(metadata, token), ErrorCode.PermissionDenied);
                await BlobArchiveActivePublicFences.HealthyAsync(administrator, adminMcp, state, token);
                await McpCallerAssertions.ErrorAsync(await official.CallAsync("keyload_blobs_metadata", metadata, token), ErrorCode.PermissionDenied, dispatched: true);
                await BlobArchiveActivePublicFences.HealthyAsync(administrator, adminMcp, state, token);
                foreach (var range in new[] { new BlobReadRequest(metadata.Blob, 1, 0, 1), new BlobReadRequest(metadata.Blob, 1, state.Published.Completed!.Value.Length, 0) })
                {
                    await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ReadBlobRangeAsync(range, token), ErrorCode.PermissionDenied);
                    await BlobArchiveActivePublicFences.HealthyAsync(administrator, adminMcp, state, token);
                    await McpCallerAssertions.ErrorAsync(await official.CallAsync("keyload_blobs_read_range", range, token), ErrorCode.PermissionDenied, dispatched: true);
                    await BlobArchiveActivePublicFences.HealthyAsync(administrator, adminMcp, state, token);
                }
                return true;
            }, token);
}
