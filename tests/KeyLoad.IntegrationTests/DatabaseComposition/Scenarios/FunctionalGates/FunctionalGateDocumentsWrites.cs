using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateDocumentsWrites
{
    internal static Task<long> CreateAsync(FunctionalGateContext context, FunctionalGateDocumentsState state, CancellationToken token)
        => CommitAsync(context,
            [new PutDocument(state.Independent.Collection, state.Independent.Id, state.IndependentJson(context.Bind(context.Anchor), "created", false), 0),
                new PutDocument(state.Related.Collection, state.Related.Id, state.RelatedJson(context.Bind(context.Anchor), true), 0)],
            [Receipt("putDocument", state.Independent, 1), Receipt("putDocument", state.Related, 1)], state.Iteration % 2 == 0, token);

    internal static Task<long> UpdateAsync(FunctionalGateContext context, FunctionalGateDocumentsState state, CancellationToken token)
        => CommitAsync(context, [new PatchDocument(state.Independent.Collection, state.Independent.Id,
            [new("/status", PatchKind.Set, "\"updated\"")], 1)],
            [Receipt("patchDocument", state.Independent, 2)], official: false, token);

    internal static Task<long> LinkAsync(FunctionalGateContext context, FunctionalGateDocumentsState state, CancellationToken token)
        => CommitAsync(context, [new PatchDocument(state.Independent.Collection, state.Independent.Id,
            [new("/anchor", PatchKind.Set, JsonSerializer.Serialize(context.Bind(context.Anchor), JsonDefaults.Options)),
                new("/related", PatchKind.Set, JsonSerializer.Serialize(state.Related, JsonDefaults.Options))], 2)],
            [Receipt("patchDocument", state.Independent, 3)], official: true, token);

    internal static Task<long> UnlinkAsync(FunctionalGateContext context, FunctionalGateDocumentsState state, CancellationToken token)
        => CommitAsync(context,
            [new PatchDocument(state.Independent.Collection, state.Independent.Id,
                [new("/anchor", PatchKind.Remove), new("/related", PatchKind.Remove)], 3),
                new PatchDocument(state.Related.Collection, state.Related.Id,
                    [new("/anchor", PatchKind.Remove), new("/related", PatchKind.Remove)], 1)],
            [Receipt("patchDocument", state.Independent, 4), Receipt("patchDocument", state.Related, 2)], official: false, token);

    internal static Task<long> DeleteAsync(FunctionalGateContext context, FunctionalGateDocumentsState state, CancellationToken token)
        => CommitAsync(context,
            [new DeleteDocument(state.Independent.Collection, state.Independent.Id, 4),
                new DeleteDocument(state.Related.Collection, state.Related.Id, 2)],
            [Receipt("deleteDocument", state.Independent, 5), Receipt("deleteDocument", state.Related, 3)], official: true, token);

    internal static async Task<long> CommitAsync(FunctionalGateContext context, ImmutableArray<Mutation> mutations,
        ImmutableArray<MutationReceipt> expected, bool official, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), context.Partition, mutations);
        var actual = official
            ? await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, command, token)
            : await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(context.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Token.Position).IsGreaterThan(0);
        await Assert.That(actual.PersistedCopies).IsEqualTo(context.ReplicaCount);
        await FunctionalGateDocumentsReads.EqualAsync(new CommitReceipt(command.CommandId, actual.Token,
            expected, DurabilityProfile.ProcessDurable, context.ReplicaCount), actual);
        var replay = official
            ? await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token))
            : await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, command, token);
        await FunctionalGateDocumentsReads.EqualAsync(actual, replay);
        return actual.Token.Position;
    }

    private static MutationReceipt Receipt(string kind, EntityRef reference, long revision)
        => new(kind, reference.Collection, reference.Id, revision);
}
