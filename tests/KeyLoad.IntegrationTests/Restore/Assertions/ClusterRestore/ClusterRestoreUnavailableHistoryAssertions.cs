using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreUnavailableHistoryAssertions
{
    private const string RetainedEventId = "purge-third";

    internal static async Task IdentityAsync(ClusterBackupOwnerReceipt actual, ClusterBackupOwnerRequest request)
    {
        await Assert.That(actual.Version).IsEqualTo(ClusterRestoreRf3Protocol.CurrentVersion);
        await Assert.That(actual.ArchiveId).IsEqualTo(request.CaptureId.ToString(ClusterRestoreRf3Protocol.ArchiveFormat));
        await Assert.That(actual.Cut.CaptureId).IsEqualTo(request.CaptureId);
        await Assert.That(actual.Cut.SourceNodeId).IsEqualTo(request.ExpectedNodeId);
        await SqlRf3Protocol.EqualAsync(request.ExpectedOwner, actual.Cut.Owner);
        await SqlRf3Protocol.EqualAsync(request.ExpectedOwner, actual.Cut.Catalog.DefaultShard);
        await Assert.That(actual.Cut.StorePosition).IsGreaterThan(0L);
        await Assert.That(actual.Cut.AppliedIndex).IsGreaterThan(0L);
        await Assert.That(actual.ManifestDigest).IsNotEmpty();
        foreach (var partition in actual.Cut.Partitions)
        {
            await Assert.That(partition.CanonicalRecordCount).IsGreaterThanOrEqualTo(0L);
            await Assert.That(partition.CanonicalDigest).IsNotEmpty();
            await Assert.That(partition.RosterDigest).IsNotEmpty();
        }
    }

    internal static async Task PageAsync(RequestCqrsRf3Callers callers,
        ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var retained = state.PurgedPage.Events.Single();
        var expected = new SourceEventRecord(state.PurgedSource, ClusterRestoreMixedRetentionRf3Protocol.PurgedTail,
            retained.EventSequence, new EventData(RetainedEventId, ClusterRestoreMixedRetentionRf3Protocol.AcceptedType,
                ClusterRestoreMixedRetentionRf3Protocol.EventPayload), retained.RecordedAt);
        var request = new ReadEventSourceRequest(state.PurgedSource,
            AfterPosition: ClusterRestoreMixedRetentionRf3Protocol.PurgePosition);
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var actual = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition,
                McpCallerTools.EventsRead, request, () => callers.Sdk.ReadEventSourceAsync(request, token), token);
            await SqlRf3Protocol.EqualAsync(state.PurgedSource, actual.Source);
            await SqlRf3Protocol.EqualAsync(new EventSourceHead(ClusterRestoreMixedRetentionRf3Protocol.PurgedTail,
                ClusterRestoreMixedRetentionRf3Protocol.PurgedTail,
                ClusterRestoreMixedRetentionRf3Protocol.InitialGeneration), actual.Head);
            await Assert.That(actual.HasMore).IsFalse();
            await Assert.That(actual.Cursor).IsNotEmpty();
            await Assert.That(actual.CutPosition).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
            await SqlRf3Protocol.EqualAsync(new[] { expected }, actual.Events.ToArray());
        }
    }
}
