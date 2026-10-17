using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeReplies
{
    internal static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers callers,
        CommandRequest command, AtomicPartitionPlacementResolution placement, long revision,
        CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Partition.AtomicPartitionId,
                receipt.Token.Position, placement.PlacementEpoch),
            [new(AtomicMembershipScopeCommands.Kind, RequestCqrsRf3Protocol.AdminCollection,
                AtomicMembershipScopeCommands.Left, revision),
             new(AtomicMembershipScopeCommands.Kind, RequestCqrsRf3Protocol.AdminCollection,
                AtomicMembershipScopeCommands.Right, revision)], DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
        return receipt;
    }

    internal static async Task<OutboxStatus> AdmitAsync(RequestCqrsRf3Callers admin,
        PartitionRef partition, OutboxStatus before, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
        await Assert.That(after.Head.Tail).IsEqualTo(checked(before.Head.Tail + 2));
        await Assert.That(after.Head.StoredRecords).IsEqualTo(checked(before.Head.StoredRecords + 2));
        await Assert.That(after.Head.StoredBytes).IsGreaterThan(before.Head.StoredBytes);
        await Assert.That(after.Head.FirstAvailable).IsEqualTo(before.Head.FirstAvailable);
        await SqlRf3Protocol.EqualAsync(before.Consumers, after.Consumers);
        return after;
    }
}
