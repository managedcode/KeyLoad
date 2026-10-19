using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Authorization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsCompositionData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionRefusals
{
    private const string DocumentFamily = "document";
    private const string EdgeFamily = "edge";
    private const string AdjacencyFamily = "adjacency";
    private const string EventFamily = "event";
    private const string StreamHeadFamily = "stream-head";
    private const string EventSequenceFamily = "event-sequence";
    private const string AnalyticsFamily = "analytics-event";
    private const string GenerationFamily = "analytics-generation";
    private const string OutboxFamily = "outbox";
    private const string OutboxHeadFamily = "outbox-head";
    private const string DocumentEpochFamily = "document-epoch";
    private const string EventIdentityFamily = "event-id";
    private const string EventFeedFamily = "event-feed";
    private const string GraphOwnerFamily = "graph-edge-owner-version";
    private const string GraphIntentFamily = "graph-cross-partition-intent";
    internal static async Task RequireAsync(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation,
        string id, ErrorCode error, string detail, IReadOnlyList<AnalyticsCompositionCommit> commits)
    {
        var before = Image(owner);
        var status = owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition);
        for (var replay = 0; replay < 2; replay++)
        {
            var actual = owner.Submit(operation);
            await O.EqualAsync(Result<object>.Fail(Errors.Problem(error, detail)), actual);
            await Assert.That(actual.Value).IsNull();
            await Assert.That(Image(owner).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
            await O.EqualAsync(status, owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition));
            await Assert.That(owner.Database.GetDocument(D.Root, new(owner.Source.Partition, D.AnchorCollection, id),
                cancellationToken: owner.CallerToken)).IsNull();
            await Assert.That(owner.Database.ReadStream(D.Root, new StreamRef(owner.Source.Partition, P.Events, id),
                cancellationToken: owner.CallerToken).Events.IsEmpty).IsTrue();
            await AnalyticsCompositionModels.RequireAsync(owner, commits);
        }
    }

    private static string[] Image(SafeProjectorOperationDatabase owner) => owner.Store.Read(view =>
    {
        string[] families = [DocumentFamily, EdgeFamily, AdjacencyFamily, EventFamily, StreamHeadFamily,
            EventSequenceFamily, AnalyticsFamily, GenerationFamily, OutboxFamily, OutboxHeadFamily,
            DocumentEpochFamily, EventIdentityFamily, EventFeedFamily, GraphOwnerFamily, GraphIntentFamily];
        var result = new List<string>();
        foreach (var family in families)
        {
            var page = view.Scan(KeySpace.Partition(family, owner.Source.Partition), 100);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The mixed-model oracle is incomplete.");
            }

            result.AddRange(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)));
        }
        return result.ToArray();
    });
}
