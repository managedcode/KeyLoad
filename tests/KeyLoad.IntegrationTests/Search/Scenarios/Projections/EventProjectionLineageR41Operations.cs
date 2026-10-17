using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class EventProjectionLineageR41Operations
{
    internal static async Task ApplyAsync(RequestCqrsRf3Callers worker, EventProjectionLineageR41State state, CancellationToken token)
    {
        state.Original = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitAsync(state.Projection, token));
        await ReceiptAsync(state, state.Projection, state.Original,
         [new(EventProjectionLineageR41Protocol.Projection, EventProjectionLineageR41Protocol.Collection, EventProjectionLineageR41Protocol.Target, 1L)]);
    }
    internal static async Task DeleteAsync(RequestCqrsRf3Callers admin, EventProjectionLineageR41State state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
         [new DeleteDocument(EventProjectionLineageR41Protocol.Collection, EventProjectionLineageR41Protocol.Source, 1L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await ReceiptAsync(state, command, receipt,
         [new(EventProjectionLineageR41Protocol.Delete, EventProjectionLineageR41Protocol.Collection, EventProjectionLineageR41Protocol.Source, 2L)]);
        state.Successes.Add((command, receipt));
        state.Deleted = true;
    }
    internal static async Task ReplaceAsync(RequestCqrsRf3Callers admin, EventProjectionLineageR41State state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
         [new PutVector(EventProjectionLineageR41Protocol.Collection,EventProjectionLineageR41Protocol.Target,
    EventProjectionLineageR41Protocol.Vector,[0f,1f],EventProjectionLineageR41Protocol.Space,1L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await ReceiptAsync(state, command, receipt,
         [new(EventProjectionLineageR41Protocol.PutVector, EventProjectionLineageR41Protocol.Collection, EventProjectionLineageR41Protocol.Target, 1L)]);
        state.Successes.Add((command, receipt));
        state.Replaced = true;
    }
    internal static async Task ReceiptAsync(EventProjectionLineageR41State state, CommandRequest request,
     CommitReceipt receipt, MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await SqlRf3Protocol.EqualAsync(new CommitToken(state.Placement.Incarnation, state.Partition.AtomicPartitionId,
         receipt.Token.Position, state.Placement.PlacementEpoch), receipt.Token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(mutations, receipt.Mutations.ToArray());
    }
}
