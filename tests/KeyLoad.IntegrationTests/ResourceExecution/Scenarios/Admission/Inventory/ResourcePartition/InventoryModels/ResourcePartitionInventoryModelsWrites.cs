using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsWrites
{
    internal static async Task<ResourcePartitionInventoryModelsBatch> RunAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        FunctionalGateContext context, ResourcePartitionInventoryModelsStep step, int ordinal,
        List<ResourcePartitionInventoryRf3Creation> creations, CancellationToken token)
    {
        var creation = new ResourcePartitionInventoryRf3Creation(Guid.NewGuid(),
            new(context.Partition.TenantId, context.Partition.DatabaseId, step.Definition));
        await SqlRf3Protocol.EqualAsync(step.Definition, await context.SdkAsync(context.Administrator.Sdk
            .ConfigureResourceAsync(creation.CommandId, creation.Request, token)));
        creations.Add(creation);
        var before = await ResourcePartitionInventoryModelsInventoryAssertions.ReadAsync(
            wave, seed, membership, step.Definition, populated: false, token);
        var placement = await context.SdkAsync(context.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(ResourcePartitionInventoryModelsProtocol.Version, context.Partition), token));
        var command = new CommandRequest(Guid.NewGuid(), context.Partition, step.Mutations, placement.PlacementEpoch);
        var receipt = ordinal % ResourcePartitionInventoryModelsProtocol.PublicProtocols == 0
            ? await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token))
            : await context.McpAsync<CommitReceipt>(context.Writer,
                KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsCommit, command, token);
        await ReceiptAsync(command, receipt, placement, step.Expected);
        var after = await ResourcePartitionInventoryModelsInventoryAssertions.ReadAsync(
            wave, seed, membership, step.Definition, populated: true, token);
        await ResourcePartitionInventoryModelsInventoryAssertions.FirstEffectAsync(before, after);
        return new(command, receipt);
    }

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt,
        AtomicPartitionPlacementResolution placement, System.Collections.Immutable.ImmutableArray<MutationReceipt> mutations)
    {
        await Assert.That(receipt.Token.Position).IsGreaterThan(ResourcePartitionInventoryModelsProtocol.MissingRevision);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            mutations, DurabilityProfile.QuorumProcessDurable), receipt);
    }
}
