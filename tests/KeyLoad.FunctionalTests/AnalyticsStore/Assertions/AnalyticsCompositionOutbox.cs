using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.Messaging;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using P = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsCompositionData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsCompositionOutbox
{
    internal static async Task RequireAsync(SafeProjectorOperationDatabase owner, AnalyticsCompositionCommit anchor,
        IReadOnlyList<AnalyticsCompositionCommit> commits)
        => await RequireAsync(owner, anchor, commits,
            AnalyticsExpectedResourceCreation.Lifetime(owner.Source, AnalyticsExpectedResourceCreation.Anchor));

    internal static async Task RequireAsync(SafeProjectorOperationDatabase owner, AnalyticsCompositionCommit anchor,
        IReadOnlyList<AnalyticsCompositionCommit> commits, ResourceLifetime anchorLifetime)
    {
        var partition = owner.Source.Partition;
        var anchorReference = AnalyticsExpectedResourceCreation.Bind(anchorLifetime, new(partition, D.AnchorCollection, D.AnchorId));
        var anchorMutation = new PutDocument(D.AnchorCollection, D.AnchorId, D.AnchorJson, 0);
        var time = new DateTimeOffset(anchor.Operation.Timestamp);
        var expected = new List<OutboxEntry> { new(1, 0, anchor.Receipt.Token, time, anchorMutation,
            new(P.Put, D.AnchorCollection, D.AnchorId, 1),
            After: new(anchorReference, 1, D.AnchorJson, new(), time)) };
        foreach (var commit in commits)
        {
            var request = OrleansSerialization.Deserialize<CommandRequest>((commit.Operation).Value!.Input.Span);
            time = new DateTimeOffset(commit.Operation.Timestamp);
            for (var ordinal = 0; ordinal < request.Mutations.Length; ordinal++)
            {
                var mutation = request.Mutations[ordinal];
                var document = mutation is PutDocument put
                    ? new DocumentRecord(AnalyticsExpectedResourceCreation.Bind(anchorLifetime, new(partition, put.Collection, put.Id)), 1, P.Json, new(), time) : null;
                expected.Add(new(expected.Count + 1, ordinal, commit.Receipt.Token, time,
                    mutation, commit.Receipt.Mutations[ordinal], After: document));
            }
        }
        await SubscriptionOldCutOutbox.EntriesAsync(owner.Database, partition, expected);
    }
}
