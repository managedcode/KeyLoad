namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateDocuments
{
    private const string BindingId = "functional-reference-binding";
    private const string BindingJson = "{\"docId\":\"functional-reference-binding\",\"kind\":\"reference-binding\"}";
    private const string PrimaryKeyColumn = "id";

    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        for (var index = 0; index < FunctionalGateDocumentsState.CollectionCount; index++)
        {
            var resource = new ResourceDefinition(FunctionalGateDocumentsState.Collection(index),
                ResourceKind.Collection, context.Partition.TransactionDomainId)
            {
                Indexes = [new("by_doc_id", ["/docId"], Unique: true)]
            };
            if (index == 0)
            {
                resource = resource with
                {
                    RelationalSchema = new(PrimaryKeyColumn, [new(PrimaryKeyColumn, RelationalColumnType.Text),
                        new("docId", RelationalColumnType.Text, Nullable: true),
                        new("kind", RelationalColumnType.Text, Nullable: true),
                        new("name", RelationalColumnType.Text, Nullable: true)]),
                    Indexes = [new("by_id", ["/id"], Unique: true), new("by_doc_id", ["/docId"], Unique: true)]
                };
            }
            var commandId = Guid.NewGuid();
            var actual = await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(commandId,
                new(context.Partition.TenantId, context.Partition.DatabaseId, resource), token));
            await FunctionalGateDocumentsReads.EqualAsync(resource, actual);
            FunctionalGateCreationLifetime.Expect(context, commandId, resource, table: false);
        }
        await FunctionalGateDocumentsReads.EqualAsync(new EntityRef(context.Partition, "gate_docs_0", "anchor"), context.Anchor);
        var mutation = new PutDocument(context.Anchor.Collection, context.Anchor.Id,
            FunctionalGateDocumentsState.AnchorJson, ExpectedRevision: 0);
        await FunctionalGateDocumentsWrites.CommitAsync(context, [mutation],
            [new("putDocument", "gate_docs_0", "anchor", 1)], official: false, token);
        await FunctionalGateEntityReferences.CaptureAsync(context, context.Administrator,
            new(context.Anchor, 1, FunctionalGateDocumentsState.AnchorJson, false, []), token);
        for (var index = 1; index < FunctionalGateDocumentsState.CollectionCount; index++)
        {
            var reference = new EntityRef(context.Partition, FunctionalGateDocumentsState.Collection(index), BindingId);
            await FunctionalGateDocumentsWrites.CommitAsync(context,
                [new PutDocument(reference.Collection, reference.Id, BindingJson, 0)],
                [new("putDocument", reference.Collection, reference.Id, 1)], official: false, token);
            await FunctionalGateEntityReferences.CaptureAsync(context, context.Administrator,
                new(reference, 1, BindingJson, false, []), token);
        }
        await FunctionalGateDocumentsReads.AnchorAsync(context, token);
    }

    internal static async Task ExerciseAsync(FunctionalGateContext context, int iteration, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iteration);
        var names = FunctionalGateDocumentsState.Create(context.Partition, iteration);
        var state = names with
        {
            Independent = context.Bind(names.Independent),
            Related = context.Bind(names.Related)
        };
        var cut = await FunctionalGateDocumentsWrites.CreateAsync(context, state, token);
        await FunctionalGateDocumentsReads.PairAsync(context, state, 1, 1, "created", false, true, cut, token);
        cut = await FunctionalGateDocumentsWrites.UpdateAsync(context, state, token);
        await FunctionalGateDocumentsReads.PairAsync(context, state, 2, 1, "updated", false, true, cut, token);
        cut = await FunctionalGateDocumentsWrites.LinkAsync(context, state, token);
        await FunctionalGateDocumentsReads.PairAsync(context, state, 3, 1, "updated", true, true, cut, token);
        cut = await FunctionalGateDocumentsWrites.UnlinkAsync(context, state, token);
        await FunctionalGateDocumentsReads.PairAsync(context, state, 4, 2, "updated", false, false, cut, token);
        cut = await FunctionalGateDocumentsWrites.DeleteAsync(context, state, token);
        await FunctionalGateDocumentsReads.AbsentAsync(context, state.Independent, cut, token);
        await FunctionalGateDocumentsReads.AbsentAsync(context, state.Related, cut, token);
        await FunctionalGateDocumentsReads.AnchorAsync(context, token);
    }
}
