using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsColdSeed
{
    private const long Absent = 0;
    private const long InitialRevision = 1;
    private const long UpdatedGeneration = 2;

    internal static async Task<ImmutableArray<FunctionalGateAnalyticsColdStore>> RunAsync(FunctionalGateContext context,
        CancellationToken token)
    {
        var stores = ImmutableArray.CreateBuilder<FunctionalGateAnalyticsColdStore>();
        for (var index = 0; index < context.ResourceCount; index++)
        {
            var state = FunctionalGateUserAnalyticsState.Create(context, index, index);
            var seed = new CommandRequest(Guid.NewGuid(), context.Partition,
                [new UpsertAnalyticsEvents(state.Store, [state.First, state.Second, state.Third],
                    Revisions((state.First.Id, Absent), (state.Second.Id, Absent), (state.Third.Id, Absent)))]);
            var seeded = await FunctionalGateAnalyticsCommands.CommitAsync(context, seed, AnalyticsStoreProtocol.UpsertKind,
                state.Store, state.Third.Id, InitialRevision, token);
            var update = new CommandRequest(Guid.NewGuid(), context.Partition,
                [new UpsertAnalyticsEvents(state.Store, [state.Updated], Revisions((state.First.Id, InitialRevision)))]);
            var updated = await FunctionalGateAnalyticsCommands.CommitAsync(context, update, AnalyticsStoreProtocol.UpsertKind,
                state.Store, state.First.Id, UpdatedGeneration, token);
            var proof = new FunctionalGateAnalyticsColdStore(state, seed, seeded, update, updated);
            await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
            stores.Add(proof);
        }
        return stores.ToImmutable();
    }

    internal static ImmutableDictionary<string, long> Revisions(params (string Id, long Revision)[] values)
        => values.ToImmutableDictionary(value => value.Id, value => value.Revision, StringComparer.Ordinal);
}
