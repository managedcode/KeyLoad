using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsActualTextReplayFlow
{
    private const int OrdinaryFrames = 2;
    private const int SingleMutation = 1;
    private const long PreviousRevision = 2;
    private const long FinalRevision = 3;
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";

    internal static async Task ReplayAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, TextIndexMaintenanceResult original, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var replay = await RequestCqrsActualTextMaintenanceFlow.ExecuteAsync(fixture, request, expectNativeWork: false, token);
        await Assert.That(replay.Checkpoint is null).IsEqualTo(original.Checkpoint is null);
        if (original.Checkpoint is not null)
        {
            var checkpoint = replay.Checkpoint ?? throw new InvalidOperationException();
            await Assert.That(NativeSerialization.Serialize(checkpoint).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(original.Checkpoint))).IsTrue();
        }
        await Assert.That(replay.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(replay.Consumer).IsEqualTo(original.Consumer);
        await Assert.That(replay.IndexGeneration).IsEqualTo(original.IndexGeneration);
        await Assert.That(replay.TrackedRecords).IsEqualTo(original.TrackedRecords);
        await Assert.That(replay.IndexSha256).IsEqualTo(original.IndexSha256);
        await Assert.That(replay.IndexedThroughSequence).IsEqualTo(original.IndexedThroughSequence);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).AsSpan().SequenceEqual(before)).IsTrue();
    }

    internal static async Task PriorParentAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest previous, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Database.Partition,
            [new PutDocument(previous.Collection, NativeTextBilingualAudit.UkrainianId,
                ChangedJson, ExpectedRevision: PreviousRevision)]);
        var receipt = await NativeTextMaintenanceCommit.ExecuteAsync<CommitReceipt>(fixture.Database,
            OperationKind.Batch, command, id, token);
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(SingleMutation);
        var current = previous with { CommandId = Guid.NewGuid() };
        var completed = await RequestCqrsActualTextMaintenanceFlow.ExecuteAsync(fixture, current, token);
        await ReplayAsync(fixture, current, completed, token);
        var before = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var refused = await RequestCqrsActualTextMaintenanceFlow.InvokeAsync(fixture, previous,
            expectNativeWork: false, token);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(refused.Payload.IsEmpty).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).AsSpan().SequenceEqual(before)).IsTrue();
        await RequestCqrsActualTextAssertions.VerifyAsync(fixture, current, changed: true, FinalRevision, token);
        var noOp = current with { CommandId = Guid.NewGuid() };
        var unchanged = await RequestCqrsActualTextMaintenanceFlow.ExecuteAsync(fixture, noOp, expectNativeWork: false, token);
        await Assert.That(unchanged.Checkpoint).IsNull();
        await ReplayAsync(fixture, noOp, unchanged, token);
        await RequestCqrsActualTextAssertions.VerifyAsync(fixture, current, changed: true, FinalRevision, token);
    }

    internal static async Task ReleaseAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest original, CancellationToken token)
    {
        var release = original with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Release };
        var actual = await RequestCqrsActualTextMaintenanceFlow.ExecuteAsync(fixture, release, expectNativeWork: false, token);
        await Assert.That(actual.CommandId).IsEqualTo(release.CommandId);
        await Assert.That(actual.Consumer).IsEqualTo(original.Consumer);
        var consumer = actual.ReleasedConsumer ?? throw new InvalidOperationException();
        await Assert.That(consumer.Consumer).IsEqualTo(original.Consumer);
        await Assert.That(consumer.Definition.IndexGeneration).IsEqualTo(original.IndexGeneration);
        await Assert.That(consumer.Released).IsTrue();
        await Assert.That(actual.Checkpoint).IsNull();
    }

    internal static async Task VerifyFramesAsync(List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        TextIndexMaintenanceMode mode, bool expectNativeWork, ErrorCode? error)
    {
        await Assert.That(frames.First().Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        if (error is not null)
        {
            await Assert.That(frames.Last().Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
            return;
        }
        await Assert.That(frames.Last().Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        if (mode == TextIndexMaintenanceMode.Release)
        { await Assert.That(frames.Count).IsEqualTo(OrdinaryFrames); return; }
        var phases = frames.Where(frame => frame.Kind == CqrsStreamChunkKind.Progress)
            .Select(frame => frame.ProgressResult!.Value.Value!.TextPhase).ToArray();
        await Assert.That(phases.First()).IsEqualTo(TextIndexMaintenancePhase.Configure);
        await Assert.That(phases.Last()).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(phases.Contains(TextIndexMaintenancePhase.Capture)).IsTrue();
        foreach (var phase in new[] { TextIndexMaintenancePhase.NativeIndex,
            TextIndexMaintenancePhase.Publish, TextIndexMaintenancePhase.Checkpoint })
        { await Assert.That(phases.Contains(phase)).IsEqualTo(expectNativeWork); }
    }
}
