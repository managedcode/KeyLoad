using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class McpDerivedMutationTestData
{
    private const string Token = "signed-transfer-token";
    private const string EventId = "projection-event";
    private const string Reducer = "projection-reducer";
    private const string Version = "v1";
    private const string TimeZone = "UTC";
    private const long Revision = 1;

    internal static ImmutableArray<Mutation> Create() =>
    [
        Projection(),
        new CreateQueueTransfer(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId,
            McpCanonicalTestData.Lane, new(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
                McpCanonicalTestData.EmptyJson)),
        new AcceptQueueTransfer(McpCanonicalTestData.Lane, Token),
        new CompleteQueueTransfer(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId, Token),
        new ConfigureRecurringSchedule(new(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId,
            DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1), TimeZone, RecurringMisfirePolicy.CatchUp,
            McpCanonicalTestData.EmptyJson, McpCanonicalTestData.EmptyJson), 0),
        new EmitRecurringOccurrences(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId, Revision),
        new CancelRecurringSchedule(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId, Revision),
        new CompareExchangeSaga(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId, 0,
            SagaPhase.Waiting, McpCanonicalTestData.EmptyJson, DateTimeOffset.UnixEpoch,
            new(McpCanonicalTestData.Lane, McpCanonicalTestData.EmptyJson, McpCanonicalTestData.EmptyJson)),
        new ExpireSaga(McpCanonicalTestData.Lane, McpCanonicalTestData.StableId, Revision)
    ];

    private static ApplyVectorProjection Projection()
        => new(new(McpCanonicalTestData.Partition, McpCanonicalTestData.Resource, McpCanonicalTestData.Entity),
            Revision, EventId, McpCanonicalTestData.Reference, Revision, McpCanonicalTestData.Field,
            Reducer, Version, Revision, new(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
                McpCanonicalTestData.Field, [1], new(Reducer, 1, DistanceMetric.Cosine, Reducer, Version), Revision));
}
