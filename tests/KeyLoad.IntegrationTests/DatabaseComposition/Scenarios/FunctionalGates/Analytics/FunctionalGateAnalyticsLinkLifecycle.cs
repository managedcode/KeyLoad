using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateAnalyticsLinkProtocol;
using R = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsLinkLifecycle
{
    internal static async Task<FunctionalGateAnalyticsLinkProof> PrepareAsync(FunctionalGateContext context,
        CancellationToken token)
    {
        // This exact row ID exists in the OLD table lifetime, then the original DROP flow creates it in the NEW lifetime.
        var literal = new DocumentResult(FunctionalGateDropRows.Reference(context, P.TableIndex, FunctionalGateDropRows.FreshId),
            P.Initial, FunctionalGateDropRows.Json(context, P.TableIndex, FunctionalGateDropRows.FreshId), false, []);
        var command = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(literal.Reference.Collection, literal.Reference.Id, literal.Json, P.Absent)]);
        var receipt = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(command, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, receipt, command.CommandId,
            [new(R.PutKind, literal.Reference.Collection, literal.Reference.Id, P.Initial)]);
        var reference = await FunctionalGateEntityReferences.CaptureAsync(context, context.Administrator, literal, token);
        var store = FunctionalGateUserAnalyticsState.StoreName(context.WriterPrincipalId, P.StoreIndex);
        var proof = new FunctionalGateAnalyticsLinkProof(store, FunctionalGateAnalyticsLinkRequests.Event(reference),
            reference, context.AnalyticsGenerations[store]);
        await FunctionalGateAnalyticsLinkCalls.RawAsync(context, FunctionalGateAnalyticsLinkRequests.Read(context, proof),
            new([], false, null), token);
        await FunctionalGateAnalyticsLinkCalls.CommitAsync(context,
            FunctionalGateAnalyticsLinkRequests.Upsert(context, proof, reference, P.Absent),
            new(AnalyticsStoreProtocol.UpsertKind, store, proof.Event.Id, checked(proof.OriginalGeneration + P.Initial)), token);
        await PageAsync(context, proof, reference, P.Initial, token);
        return proof;
    }

    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateDropState state,
        FunctionalGateAnalyticsLinkProof proof, CancellationToken token)
    {
        var fresh = state.Tables.Single(table => table.Index == P.TableIndex).Fresh.Reference;
        await Assert.That(fresh.Id).IsEqualTo(proof.OriginalReference.Id);
        await Assert.That(fresh.Collection).IsEqualTo(proof.OriginalReference.Collection);
        await Assert.That(fresh.Lifetime).IsNotEqualTo(proof.OriginalReference.Lifetime);
        Task Unchanged() => UnchangedAsync(context, state, proof, token);
        await FunctionalGateAnalyticsLinkRefusals.ReadAsync(context,
            FunctionalGateAnalyticsLinkRequests.Read(context, proof), Unchanged, token);
        await FunctionalGateAnalyticsLinkRefusals.WriteAsync(context,
            FunctionalGateAnalyticsLinkRequests.Upsert(context, proof, proof.OriginalReference, P.Initial), Unchanged, token);
        // A fresh name is bound once during the real write; its returned persisted link must equal the new server-issued D1.
        await FunctionalGateAnalyticsLinkCalls.CommitAsync(context,
            FunctionalGateAnalyticsLinkRequests.Upsert(context, proof, fresh with
            {
                Lifetime = null
            }, P.Initial),
            new(AnalyticsStoreProtocol.UpsertKind, proof.Store, proof.Event.Id, checked(proof.OriginalGeneration + P.Updated)), token);
        await PageAsync(context, proof, fresh, P.Updated, token);
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        await FunctionalGateAnalyticsLinkCalls.CommitAsync(context, FunctionalGateAnalyticsLinkRequests.Delete(context, proof),
            new(AnalyticsStoreProtocol.DeleteKind, proof.Store, proof.Event.Id, checked(proof.OriginalGeneration + P.GenerationWrites)), token);
        await FunctionalGateAnalyticsLinkCalls.RawAsync(context, FunctionalGateAnalyticsLinkRequests.Read(context, proof),
            new([], false, null), token);
        await FunctionalGateAnalyticsLinkCalls.AggregateAsync(context, proof, new([], P.Absent), token);
        context.AnalyticsGenerations[proof.Store] = checked(proof.OriginalGeneration + P.GenerationWrites);
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
    }

    private static async Task UnchangedAsync(FunctionalGateContext context, FunctionalGateDropState state,
        FunctionalGateAnalyticsLinkProof proof, CancellationToken token)
    {
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        // Aggregate has no reference-resolution output; all five independent arithmetic controls stay intact.
        await FunctionalGateAnalyticsLinkCalls.AggregateAsync(context, proof,
            FunctionalGateAnalyticsLinkRequests.ExpectedAggregate, token);
    }

    private static async Task PageAsync(FunctionalGateContext context, FunctionalGateAnalyticsLinkProof proof,
        EntityRef expectedReference, long revision, CancellationToken token)
    {
        var value = proof.Event;
        var expected = new AnalyticsEventRecord(value.Id, value.Timestamp, value.Dimensions, value.Metrics,
            value.RowAccess, [expectedReference], revision);
        await FunctionalGateAnalyticsLinkCalls.RawAsync(context, FunctionalGateAnalyticsLinkRequests.Read(context, proof),
            new([expected], false, null), token);
        await FunctionalGateAnalyticsLinkCalls.AggregateAsync(context, proof,
            FunctionalGateAnalyticsLinkRequests.ExpectedAggregate, token);
    }
}
