using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateUserAnalyticsWrites
{
    internal static Task<long> UpsertAsync(FunctionalGateContext context, FunctionalGateUserAnalyticsState state,
        ImmutableArray<AnalyticsEventData> events, ImmutableDictionary<string, long> revisions,
        string lastId, long previousGeneration, CancellationToken token)
        => CommitAsync(context, new UpsertAnalyticsEvents(state.Store, events, revisions),
            AnalyticsStoreProtocol.UpsertKind, lastId, previousGeneration, token);

    internal static Task<long> DeleteAsync(FunctionalGateContext context, FunctionalGateUserAnalyticsState state,
        ImmutableDictionary<string, long> revisions, string lastId, long previousGeneration, CancellationToken token)
        => CommitAsync(context, new DeleteAnalyticsEvents(state.Store, revisions),
            AnalyticsStoreProtocol.DeleteKind, lastId, previousGeneration, token);

    private static async Task<long> CommitAsync(FunctionalGateContext context, Mutation mutation,
        string kind, string lastId, long previousGeneration, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), context.Partition, [mutation]);
        var original = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await Assert.That(original.Mutations.Length).IsEqualTo(P.FirstRevision);
        var effect = original.Mutations.Single();
        await Assert.That(effect.Kind).IsEqualTo(kind);
        await Assert.That(effect.Resource).IsEqualTo(mutation.Resource);
        await Assert.That(effect.Id).IsEqualTo(lastId);
        await Assert.That(effect.Revision).IsEqualTo(checked(previousGeneration + P.FirstRevision));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, original, command.CommandId,
            [new(kind, mutation.Resource, lastId, effect.Revision)], token);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, FunctionalGateAnalyticsIdentity.Wire(command), token), original);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: true, token), original);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: false, token), original);
        return effect.Revision;
    }
}
