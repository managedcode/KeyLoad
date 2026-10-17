using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3PolicyReplay
{
    private const string CurrentEpochId = "current-policy-epoch";
    private const string CurrentEpochJson = "{\"private\":\"d2-inventory-private-payload\",\"value\":41}";

    internal static Task VerifyAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
                var before = await SnapshotAsync(wave, seed, membership, token);
                foreach (var creation in seed.Creations)
                {
                    await ResourcePartitionInventoryPolicyRefusal.CreationAsync(callers, creation, token);
                }
                foreach (var original in seed.Originals)
                {
                    await ResourcePartitionInventoryPolicyRefusal.SdkAsync(
                        await callers.Sdk.CommitAsync(original.Command, token));
                    await ResourcePartitionInventoryPolicyRefusal.McpAsync(await callers.Mcp.CallAsync(
                        McpCallerTools.DocumentsCommit, original.Command, token));
                    await ResourcePartitionInventoryRf3PublicAssertions.DocumentAsync(
                        callers, original.Document.Reference, original.Document, token);
                }
                await ResourcePartitionInventoryPolicyRefusal.SdkAsync(
                    await callers.Sdk.CommitAsync(seed.LateFailure, token));
                await ResourcePartitionInventoryPolicyRefusal.McpAsync(await callers.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, seed.LateFailure, token));
                await ResourcePartitionInventoryRf3PublicAssertions.DocumentAsync(callers,
                    new(seed.LateFailure.Partition, ResourcePartitionInventoryRf3Protocol.First,
                        ResourcePartitionInventoryRf3Protocol.LateId), null, token);
                await ResourcePartitionInventoryPolicyRefusal.UnchangedAsync(before,
                    await SnapshotAsync(wave, seed, membership, token));
                return true;
            }, token);

    internal static async Task HealthyAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, CancellationToken token)
    {
        var original = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, seed.Credential,
            callers => ResourcePartitionInventoryRf3Corpus.WriteAsync(callers,
                seed.Originals[0].Command.Partition, ResourcePartitionInventoryRf3Protocol.First,
                CurrentEpochId, CurrentEpochJson, official: false, token), token);
        await ResourcePartitionInventoryRf3PublicAssertions.ReplayAsync(wave,
            seed with
            {
                Creations = [],
                Originals = [original]
            }, token);
        // A new identity exercises the original rollback independently of the denied old outcome.
        await ResourcePartitionInventoryRf3PublicAssertions.LateFailureAsync(wave,
            seed with
            {
                LateFailure = seed.LateFailure with
                {
                    CommandId = Guid.NewGuid()
                }
            }, token);
    }

    private static async Task<ResourceOwnerInventoryResultV1> SnapshotAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership, CancellationToken token)
        => await ResourcePartitionInventoryRf3Assertions.SuccessAsync(
            await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, seed, membership,
                ResourcePartitionInventoryRf3Protocol.First, token));
}
