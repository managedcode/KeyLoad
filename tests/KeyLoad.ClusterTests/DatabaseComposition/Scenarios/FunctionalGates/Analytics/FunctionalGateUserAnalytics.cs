using System.Collections.Immutable;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Three first-class user analytics stores, alongside the distinct series and operational modules.</summary>
internal static class FunctionalGateUserAnalytics
{
    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        for (var index = P.Empty; index < context.ResourceCount; index++)
        {
            var expected = new ResourceDefinition(FunctionalGateUserAnalyticsState.StoreName(context.WriterPrincipalId, index),
                ResourceKind.AnalyticsStore, context.Partition.TransactionDomainId)
            {
                AnalyticsSchema = P.Schema,
                FieldPolicies = FunctionalGateAnalyticsAccess.Policies
            };
            var actual = await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(context.Partition.TenantId, context.Partition.DatabaseId, expected), token));
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
            context.AnalyticsGenerations.Add(expected.Name, P.Empty);
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext context, int iteration, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iteration);
        await FunctionalGateDocumentsReads.AnchorAsync(context, token);
        for (var index = P.Empty; index < context.ResourceCount; index++)
        {
            await StoreAsync(context, FunctionalGateUserAnalyticsState.Create(context, index, iteration), token);
        }
    }

    private static async Task StoreAsync(FunctionalGateContext context, FunctionalGateUserAnalyticsState state, CancellationToken token)
    {
        var denied = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new UpsertAnalyticsEvents(state.Store, [state.First], Revisions((state.First.Id, P.Empty)))]);
        await FunctionalGateUserAssertions.RefusedWriteAsync(context.Reader, denied, null, token);
        await FunctionalGateUserAnalyticsReads.VerifyAsync(context, state, [], new([], P.Empty), token);
        var generation = await FunctionalGateUserAnalyticsWrites.UpsertAsync(context, state,
            [state.First, state.Second, state.Third], Revisions((state.First.Id, P.Empty),
                (state.Second.Id, P.Empty), (state.Third.Id, P.Empty)), state.Third.Id, context.AnalyticsGenerations[state.Store], token);
        await FunctionalGateUserAnalyticsReads.VerifyAsync(context, state,
            [FunctionalGateUserAnalyticsState.Record(state.First, P.FirstRevision),
                FunctionalGateUserAnalyticsState.Record(state.Second, P.FirstRevision),
                FunctionalGateUserAnalyticsState.Record(state.Third, P.FirstRevision)],
            FunctionalGateUserAnalyticsExpected.Initial, token);
        await FunctionalGateUserAnalyticsReads.PageAsync(context, state, token);
        generation = await FunctionalGateUserAnalyticsWrites.UpsertAsync(context, state,
            [state.Updated], Revisions((state.First.Id, P.FirstRevision)), state.First.Id, generation, token);
        await FunctionalGateUserAnalyticsReads.VerifyAsync(context, state,
            [FunctionalGateUserAnalyticsState.Record(state.Updated, P.UpdatedRevision),
                FunctionalGateUserAnalyticsState.Record(state.Second, P.FirstRevision),
                FunctionalGateUserAnalyticsState.Record(state.Third, P.FirstRevision)],
            FunctionalGateUserAnalyticsExpected.Updated, token);
        generation = await FunctionalGateUserAnalyticsWrites.DeleteAsync(context, state,
            Revisions((state.Second.Id, P.FirstRevision)), state.Second.Id, generation, token);
        await FunctionalGateUserAnalyticsReads.VerifyAsync(context, state,
            [FunctionalGateUserAnalyticsState.Record(state.Updated, P.UpdatedRevision),
                FunctionalGateUserAnalyticsState.Record(state.Third, P.FirstRevision)],
            FunctionalGateUserAnalyticsExpected.DeletedSecond, token);
        generation = await FunctionalGateUserAnalyticsWrites.DeleteAsync(context, state,
            Revisions((state.First.Id, P.UpdatedRevision), (state.Third.Id, P.FirstRevision)), state.Third.Id, generation, token);
        await FunctionalGateUserAnalyticsReads.VerifyAsync(context, state, [], new([], P.Empty), token);
        context.AnalyticsGenerations[state.Store] = generation;
    }

    private static ImmutableDictionary<string, long> Revisions(params (string Id, long Revision)[] values)
        => values.ToImmutableDictionary(value => value.Id, value => value.Revision, StringComparer.Ordinal);
}
