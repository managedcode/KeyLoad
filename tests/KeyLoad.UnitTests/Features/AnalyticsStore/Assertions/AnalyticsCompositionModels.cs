using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.Authorization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsCompositionData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionModels
{
    internal static async Task RequireAsync(SafeProjectorOperationDatabase owner,
        IReadOnlyList<AnalyticsCompositionCommit> commits)
    {
        var partition = owner.Source.Partition;
        await O.EqualAsync(new DocumentResult(new(partition, D.AnchorCollection, D.AnchorId), 1, D.AnchorJson, false, []),
            owner.Database.GetDocument(D.Root, new(partition, D.AnchorCollection, D.AnchorId), cancellationToken: owner.CallerToken));
        foreach (var commit in commits)
        {
            var request = KeyLoad.Core.Features.InternalSerialization.NativeCommandPayload.Read<CommandRequest>(commit.Operation);
            var id = ((PutDocument)request.Mutations[0]).Id;
            var reference = new EntityRef(partition, D.AnchorCollection, id);
            await O.EqualAsync(new DocumentResult(reference, 1, P.Json, false, []),
                owner.Database.GetDocument(D.Root, reference, cancellationToken: owner.CallerToken));
            var graph = owner.Database.Traverse(D.Root, partition, P.Graph, reference, maxDepth: 1, cancellationToken: owner.CallerToken);
            await O.EqualAsync(new global::KeyLoad.GraphTraversal([new(partition, D.AnchorCollection, D.AnchorId), reference],
                [new(id, reference, new(partition, D.AnchorCollection, D.AnchorId), P.Label, P.EmptyJson, 1)]), graph);
            var stream = new StreamRef(partition, P.Events, id);
            var page = owner.Database.ReadStream(D.Root, stream, cancellationToken: owner.CallerToken);
            var sequence = commits.ToList().IndexOf(commit) + 1;
            await O.EqualAsync(new StreamPage(stream, new(1, 1, 1),
                [new(stream, 1, sequence, new(id, P.Created, P.EmptyJson), (owner.Entry(commit.Operation).Operation ?? throw new InvalidOperationException("The retained test operation is missing.")).EvaluatedAt)],
                owner.Store.Position, false, null, owner.Store.Position), page);
        }
        var expected = commits.Select(commit => ((PutDocument)KeyLoad.Core.Features.InternalSerialization.NativeCommandPayload.Read<CommandRequest>(commit.Operation).Mutations[0]).Id)
            .Order(StringComparer.Ordinal).Select(id => D.Event(owner.Source, id, D.Alpha, 7)).ToArray();
        var records = expected.Select(value => new AnalyticsEventRecord(value.Id, value.Timestamp, value.Dimensions,
            value.Metrics, value.RowAccess, value.Links, 1)).ToImmutableArray();
        await O.EqualAsync(new AnalyticsEventsPage(records, false, null),
            owner.Database.ReadAnalyticsEvents(D.Root, D.Read(owner.Source), owner.CallerToken));
        var snapshot = owner.Database.CaptureAnalyticsAggregate(D.Root, D.Query(owner.Source), owner.CallerToken);
        await AnalyticsStoreNativeTestProjection.RunAsync(snapshot,
            new([D.Row(D.Alpha, commits.Count, commits.Count * 7, 7, 7, 7)], commits.Count), owner.CallerToken);
        owner.Database.ValidateAnalyticsAggregatePublication(snapshot);
    }
}
