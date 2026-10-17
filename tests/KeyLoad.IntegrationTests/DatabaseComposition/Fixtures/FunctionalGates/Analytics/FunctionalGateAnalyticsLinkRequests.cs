using System.Collections.Immutable;
using A = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateAnalyticsLinkProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsLinkRequests
{
    internal static AnalyticsEventData Event(EntityRef reference)
    {
        var id = P.Prefix + Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat);
        return new(id, P.From,
            ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal)
                .Add(A.Cohort, id).Add(A.Kind, A.Alpha),
            ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal)
                .Add(A.Value, P.Value).Add(A.Weight, A.WeightValue), null, [reference]);
    }

    internal static CommandRequest Upsert(FunctionalGateContext context, FunctionalGateAnalyticsLinkProof proof,
        EntityRef reference, long expectedRevision)
        => new(Guid.NewGuid(), context.Partition, [new UpsertAnalyticsEvents(proof.Store,
            [proof.Event with { Links = [reference] }], Revisions(proof, expectedRevision))]);

    internal static CommandRequest Delete(FunctionalGateContext context, FunctionalGateAnalyticsLinkProof proof)
        => new(Guid.NewGuid(), context.Partition, [new DeleteAnalyticsEvents(proof.Store, Revisions(proof, P.Updated))]);

    internal static ReadAnalyticsEventsRequest Read(FunctionalGateContext context, FunctionalGateAnalyticsLinkProof proof)
        => new(context.Partition, proof.Store, P.From, P.Until, A.PageLimit, null);

    internal static AggregateAnalyticsRequest Aggregate(FunctionalGateContext context, FunctionalGateAnalyticsLinkProof proof)
        => new(context.Partition, proof.Store, P.From, P.Until, [A.Kind], A.Selections, A.MaximumGroups);

    internal static AnalyticsAggregateResult ExpectedAggregate => new([new(
        ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal).Add(A.Kind, A.Alpha),
        ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal).Add(A.Count, P.Initial)
            .Add(A.Sum, P.Value).Add(A.Average, P.Value).Add(A.Minimum, P.Value).Add(A.Maximum, P.Value))], P.Initial);

    private static ImmutableDictionary<string, long> Revisions(FunctionalGateAnalyticsLinkProof proof, long revision)
        => ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal).Add(proof.Event.Id, revision);
}
