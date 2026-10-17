using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsTable
{
    internal static async Task<ResourcePartitionInventoryModelsTableOriginal> CreateAsync(
        TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, FunctionalGateContext context, CancellationToken token)
    {
        var create = new SqlOperationRequest(context.Partition, ResourcePartitionInventoryModelsProtocol.TableCreateSql,
            CommandId: Guid.NewGuid());
        var definition = await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(
            context, context.Administrator, create, token);
        await SqlRf3Protocol.EqualAsync(ResourcePartitionInventoryModelsProtocol.TableDefinition, definition);
        var before = await ResourcePartitionInventoryModelsInventoryAssertions.ReadAsync(
            wave, seed, membership, definition, populated: false, token);
        var insert = FunctionalGateColdProtocol.Insert(context.Partition, ResourcePartitionInventoryModelsProtocol.Index,
            ResourcePartitionInventoryModelsProtocol.Document);
        var placement = await context.SdkAsync(context.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(ResourcePartitionInventoryModelsProtocol.Version, context.Partition), token));
        var receipt = await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, insert, token);
        var expected = new CommitReceipt(insert.CommandId!.Value,
            new(placement.Incarnation, context.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new(ResourcePartitionInventoryModelsProtocol.PutKind, definition.Name, FunctionalGateColdProtocol.Id,
                ResourcePartitionInventoryModelsProtocol.InitialRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.Position).IsGreaterThan(ResourcePartitionInventoryModelsProtocol.MissingRevision);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
        var after = await ResourcePartitionInventoryModelsInventoryAssertions.ReadAsync(
            wave, seed, membership, definition, populated: true, token);
        await ResourcePartitionInventoryModelsInventoryAssertions.FirstEffectAsync(before, after);
        return new(create, definition, insert, receipt);
    }
}
