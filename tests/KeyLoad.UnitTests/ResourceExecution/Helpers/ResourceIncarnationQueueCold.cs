using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceIncarnationQueueCold
{
    internal static async Task RunAsync(TestDatabase fixture, ResourceDefinition definition, byte[] lifetime,
        MessageInspection warm, List<(ReplicatedOperation Command, OperationResult Result)> replays, CancellationToken token)
    {
        var lane = new QueueLaneRef(fixture.Partition, ResourceIncarnationQueueFlow.Queue);
        var identity = fixture.Store.Identity;
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        fixture.JoinOwnedResources();
        for (var wave = 0; wave < 2; wave++)
        {
            token.ThrowIfCancellationRequested();
            await ResourceIncarnationCold.OpenAsync(fixture, identity, async store =>
            {
                await ResourceIncarnationAssertions.UnchangedAsync(store, image, position);
                var database = fixture.CreateConfiguredDatabase(store);
                await ResourceIncarnationAssertions.EqualAsync(definition, store.Read(view => view.GetRecord<ResourceDefinition>(
                    KeySpace.Resource(lane.Partition.TenantId, lane.Partition.DatabaseId, lane.Queue)))!);
                await ResourceIncarnationQueueFlow.PreserveAsync(store, lane, lifetime);
                await ResourceIncarnationQueueFlow.ReplayAsync(database, store, replays);
                await ResourceIncarnationAssertions.EqualAsync(warm, database.InspectMessage("root", lane, "warm")!);
                var id = "cold-" + wave.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await ResourceIncarnationQueueFlow.EnqueueAsync(database, lane, id, wave + 2);
                await ResourceIncarnationQueueFlow.CompleteAsync(database, lane, id, wave + 2);
                await ResourceIncarnationQueueFlow.PreserveAsync(store, lane, lifetime);
                image = QueueWholeFlowStorage.Bytes(store);
                position = store.Position;
            });
        }
    }
}
