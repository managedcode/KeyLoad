using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static Task SeedReceiptAsync(RequestCqrsHostedDatabase database, CommandRequest command, CommitReceipt receipt, long position)
        => ReceiptAsync(database, command, receipt, position,
            [new(RequestCqrsAnnTestData.PutDocumentKind, RequestCqrsAnnTestData.Collection, RequestCqrsAnnTestData.First, RequestCqrsAnnTestData.OriginalRevision),
             new(RequestCqrsAnnTestData.PutVectorKind, RequestCqrsAnnTestData.Collection, RequestCqrsAnnTestData.First, RequestCqrsAnnTestData.OriginalRevision),
             new(RequestCqrsAnnTestData.PutDocumentKind, RequestCqrsAnnTestData.Collection, RequestCqrsAnnTestData.Second, RequestCqrsAnnTestData.OriginalRevision),
             new(RequestCqrsAnnTestData.PutVectorKind, RequestCqrsAnnTestData.Collection, RequestCqrsAnnTestData.Second, RequestCqrsAnnTestData.OriginalRevision)]);

    internal static async Task ReceiptAsync(RequestCqrsHostedDatabase database, CommandRequest command,
        CommitReceipt receipt, long position, MutationReceipt[] mutations)
    {
        var placement = database.Database.ReadAtomicPartitionPlacement(RequestCqrsAnnTestData.Principal,
            new(RequestCqrsAnnTestData.Version, database.Partition));
        await EqualAsync(new CommitReceipt(command.CommandId,
            new(database.Store.Identity.Incarnation, database.Partition.AtomicPartitionId, position, placement.PlacementEpoch),
            [.. mutations], database.Store.Identity.Durability), receipt);
    }

    internal static async Task FramesAsync(List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        AnnMaintenanceRequest request, Guid requestId)
    {
        await BoundaryAsync(frames, requestId);
        if (request.Mode == AnnMaintenanceMode.Release)
        {
            await Assert.That(frames.Count).IsEqualTo(RequestCqrsAnnTestData.OrdinaryFrameCount);
            return;
        }
        var progress = frames.Where(frame => frame.Kind == CqrsStreamChunkKind.Progress).ToArray();
        var phases = progress.Select(frame => frame.ProgressResult!.Value.Value!.AnnPhase).ToArray();
        await Assert.That(phases.Where(phase => phase != AnnMaintenancePhase.Replay)
            .SequenceEqual(RequestCqrsAnnTestData.Phases.Select(phase => (AnnMaintenancePhase?)phase))).IsTrue();
        var configured = false;
        var captured = false;
        var verified = false;
        foreach (var frame in progress)
        {
            await Assert.That(frame.ProgressResult!.Value.IsSuccess).IsTrue();
            await Assert.That(frame.ProgressResult!.Value.Problem).IsNull();
            var phase = frame.ProgressResult!.Value.Value!.AnnPhase!.Value;
            if (phase == AnnMaintenancePhase.Configure)
            {
                configured = true;
            }
            if (phase == AnnMaintenancePhase.Capture)
            {
                captured = true;
            }
            if (phase == AnnMaintenancePhase.Verify)
            {
                verified = true;
            }
            if (phase == AnnMaintenancePhase.Replay)
            {
                await Assert.That(configured && captured && !verified).IsTrue();
            }
            await EqualAsync(new GrainRequestProgress(requestId) { AnnPhase = phase }, frame.ProgressResult!.Value.Value!);
            await Assert.That(frame.Message).IsEqualTo(AnnMaintenanceProgress.PhaseCompleted);
            await Assert.That(frame.EventId).IsNull();
            await Assert.That(frame.Final).IsNull();
            await Assert.That(frame.EventType).IsEqualTo(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(frame.Kind));
        }
    }

    internal static async Task RetainedFramesAsync(
        List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames, Guid requestId)
    {
        await Assert.That(frames.Count).IsEqualTo(RequestCqrsAnnTestData.OrdinaryFrameCount + RequestCqrsAnnTestData.OneStep);
        await BoundaryAsync(frames, requestId);
        var progress = frames.Single(frame => frame.Kind == CqrsStreamChunkKind.Progress);
        var result = progress.ProgressResult ?? throw new InvalidOperationException();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Problem).IsNull();
        await EqualAsync(new GrainRequestProgress(requestId) { AnnPhase = AnnMaintenancePhase.Completed },
            result.Value ?? throw new InvalidOperationException());
        await Assert.That(progress.Message).IsEqualTo(AnnMaintenanceProgress.PhaseCompleted);
        await Assert.That(progress.EventId).IsNull();
        await Assert.That(progress.Final).IsNull();
        await Assert.That(progress.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(progress.Kind));
    }

    private static async Task BoundaryAsync(List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        Guid requestId)
    {
        var first = frames.First();
        var last = frames.Last();
        await Assert.That(first.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(first.ProgressResult!.Value.IsSuccess).IsTrue();
        await Assert.That(first.ProgressResult.Value.Problem).IsNull();
        await EqualAsync(new GrainRequestProgress(requestId), first.ProgressResult.Value.Value!);
        await Assert.That(first.Final).IsNull();
        await Assert.That(first.Message).IsNull();
        await Assert.That(first.EventId).IsNull();
        await Assert.That(first.EventType).IsEqualTo(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(first.Kind));
        await Assert.That(last.Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(last.ProgressResult).IsNull();
        await Assert.That(last.Final!.Value.IsSuccess).IsTrue();
        await Assert.That(last.Final.Value.Problem).IsNull();
        await Assert.That(last.Message).IsNull();
        await Assert.That(last.EventId).IsNull();
        await Assert.That(last.EventType).IsEqualTo(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(last.Kind));
        for (var index = RequestCqrsAnnTestData.FirstOffset; index < frames.Count; index++)
        {
            await Assert.That(frames[index].Sequence).IsEqualTo(index + RequestCqrsAnnTestData.OneStep);
        }
    }

}
