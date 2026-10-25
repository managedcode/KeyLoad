using System.Text.Json;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateCompositionProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Exercises actual SQL typed INSERT and atomic document/typed-row link, unlink and delete.</summary>
internal static class FunctionalGateComposition
{
    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        await Assert.That(context.ResourceCount).IsEqualTo(P.Resources);
        await FunctionalGateCompositionAssertions.AnchorAsync(context, token);
    }

    internal static async Task ExerciseAsync(FunctionalGateContext context, int iteration, CancellationToken token)
    {
        var state = FunctionalGateCompositionState.Create(context, iteration);
        var insert = state.Insert(context);
        var inserted = await FunctionalGateCompositionCalls.SqlAsync<CommitReceipt>(context, insert, false, true, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, inserted, insert.CommandId!.Value,
            [new(P.Put, P.Table, state.Id, P.Initial)]);
        var link = new CommandRequest(Guid.NewGuid(), context.Partition,
        [new PutDocument(state.Collection, state.Id, state.Document(context, true), P.Absent),
         new PatchDocument(P.Table, state.Id, [new("/doc_id", PatchKind.Set,
             JsonSerializer.Serialize(state.Id))], P.Initial)]);
        var linked = await FunctionalGateCompositionCalls.CommandAsync(context, link, true, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, linked, link.CommandId,
            [new(P.Put, state.Collection, state.Id, P.Initial), new(P.Patch, P.Table, state.Id, P.LinkedTable)]);
        await FunctionalGateCompositionAssertions.PairAsync(context, state, linked, true, token);
        var unlink = new CommandRequest(Guid.NewGuid(), context.Partition,
        [new PatchDocument(state.Collection, state.Id, [new("/typed", PatchKind.Remove)], P.Initial),
         new PatchDocument(P.Table, state.Id, [new("/doc_id", PatchKind.Set,
             JsonSerializer.Serialize(context.Anchor.Id))], P.LinkedTable)]);
        var unlinked = await FunctionalGateCompositionCalls.CommandAsync(context, unlink, false, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, unlinked, unlink.CommandId,
            [new(P.Patch, state.Collection, state.Id, P.UnlinkedDocument), new(P.Patch, P.Table, state.Id, P.UnlinkedTable)]);
        await FunctionalGateCompositionAssertions.PairAsync(context, state, unlinked, false, token);
        var delete = new CommandRequest(Guid.NewGuid(), context.Partition,
        [new DeleteDocument(state.Collection, state.Id, P.UnlinkedDocument),
         new DeleteDocument(P.Table, state.Id, P.UnlinkedTable)]);
        var deleted = await FunctionalGateCompositionCalls.CommandAsync(context, delete, true, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, deleted, delete.CommandId,
            [new(P.Delete, state.Collection, state.Id, P.DeletedDocument), new(P.Delete, P.Table, state.Id, P.DeletedTable)]);
        await FunctionalGateCompositionAssertions.MissingAsync(context, state, deleted, token);
    }
}
