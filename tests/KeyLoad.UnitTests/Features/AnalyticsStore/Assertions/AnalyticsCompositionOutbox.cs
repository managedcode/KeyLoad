using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.Messaging;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsCompositionData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionOutbox
{
    internal static async Task RequireAsync(SafeProjectorOperationDatabase owner, AnalyticsCompositionCommit anchor,
        IReadOnlyList<AnalyticsCompositionCommit> commits)
    {
        var partition = owner.Source.Partition;
        var anchorMutation = new PutDocument(D.AnchorCollection, D.AnchorId, D.AnchorJson, 0);
        var time = owner.Entry(anchor.Operation).Operation.EvaluatedAt;
        var expected = new List<OutboxEntry> { new(1, 0, anchor.Receipt.Token, time, anchorMutation,
            new(P.Put, D.AnchorCollection, D.AnchorId, 1),
            After: new(new(partition, D.AnchorCollection, D.AnchorId), 1, D.AnchorJson, new(), time)) };
        foreach (var commit in commits)
        {
            var request = KeyLoad.Core.Features.InternalSerialization.NativeCommandPayload.Read<CommandRequest>(commit.Operation);
            time = owner.Entry(commit.Operation).Operation.EvaluatedAt;
            for (var ordinal = 0; ordinal < request.Mutations.Length; ordinal++)
            {
                var mutation = request.Mutations[ordinal];
                var document = mutation is PutDocument put
                    ? new DocumentRecord(new(partition, put.Collection, put.Id), 1, P.Json, new(), time) : null;
                expected.Add(new(expected.Count + 1, ordinal, commit.Receipt.Token, time,
                    mutation, commit.Receipt.Mutations[ordinal], After: document));
            }
        }
        await SubscriptionOldCutOutbox.EntriesAsync(owner.Database, partition, expected);
    }
}
