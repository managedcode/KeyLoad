using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using P = KeyLoad.UnitTests.Features.Authorization.StartedReadPolicyProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class StartedReadPolicyAssertions
{
    internal static async Task ReceiptAsync(ConnectionNativeScenario scenario, CommandRequest command,
        GrainOperationReply reply)
    {
        await Assert.That(reply.Error).IsNull();
        var receipt = ConnectionNativeAssertions.Value<CommitReceipt>(reply);
        var store = scenario.Fixture.Database.Store;
        var entry = scenario.Fixture.Database.ReadRetainedEntry(command.CommandId);
        CommitReceipt expected = new(command.CommandId,
            new(store.Identity.Incarnation, command.Partition.AtomicPartitionId, entry.Index, P.PlacementEpoch),
            [new(P.Put, ConnectionNativeProtocol.Collection, P.Document, P.Revision)], DurabilityProfile.ProcessDurable);
        await EqualAsync(expected, receipt);
        await Assert.That(receipt.Token.Position).IsGreaterThan(P.EmptyPosition);
        await Assert.That(receipt.Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
    }

    internal static async Task DocumentAsync(ConnectionNativeScenario scenario, GrainOperationReply reply)
    {
        await Assert.That(reply.Error).IsNull();
        DocumentResult expected = new(scenario.Reference(P.Document), P.Revision, ConnectionNativeProtocol.FirstJson, false, []);
        await EqualAsync(expected, ConnectionNativeAssertions.Value<DocumentResult>(reply));
        await EqualAsync(expected, scenario.Stored(P.Document)!);
    }

    internal static async Task FramesAsync(StartedReadPolicyObservation observation, Guid requestId, string variant)
    {
        await Assert.That(observation.Frames.Count).IsGreaterThanOrEqualTo(P.StartedFrames);
        var started = observation.Frames[P.EmptyFrames];
        var expectedStarted = CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.Started(
            Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(requestId)), sequence: P.StartedSequence);
        await EqualAsync(expectedStarted, started);
        await Assert.That(observation.Frames.Any(frame => frame.Kind is CqrsStreamChunkKind.Progress or CqrsStreamChunkKind.Completed)).IsFalse();
        if (observation.OriginalCancellation is not null)
        {
            await Assert.That(variant).IsEqualTo(P.Cancelled);
            await Assert.That(observation.Frames.Count).IsEqualTo(P.StartedFrames);
            return;
        }
        await Assert.That(observation.Frames.Count).IsEqualTo(P.FailedFrames);
        var failed = observation.Frames[P.StartedFrames];
        var code = variant == P.Cancelled ? ErrorCode.Cancelled : ErrorCode.Unauthenticated;
        await Assert.That(failed.Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
        await Assert.That(failed.Sequence).IsEqualTo(P.FailedSequence);
        await Assert.That(failed.ProgressResult.HasValue).IsFalse();
        await Assert.That(failed.Final.HasValue).IsTrue();
        await Assert.That(failed.Final!.Value.IsFailed).IsTrue();
        await Assert.That(failed.Final.Value.Value).IsNull();
        await Assert.That(failed.Final.Value.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(failed.Final.Value.Problem.StatusCode).IsEqualTo(Errors.Status(code));
        await EqualAsync(Errors.Problem(code, variant == P.Cancelled ? P.CancellationDetail : P.Unavailable),
            failed.Final.Value.Problem);
        await Assert.That(failed.EventType).IsEqualTo(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Failed));
        await Assert.That(failed.Message).IsNull();
        await Assert.That(failed.EventId).IsNull();
    }

    internal static async Task ConservedAsync(ConnectionNativeScenario scenario, string[] image, long position,
        byte[] originalOutcome, string writer, CommandRequest command)
    {
        var store = scenario.Fixture.Database.Store;
        await Assert.That(QueueWholeFlowStorage.Bytes(store).AsSpan().SequenceEqual(image)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        var actual = store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(command.Partition, writer, command.CommandId)))!;
        await Assert.That(actual.AsSpan().SequenceEqual(originalOutcome)).IsTrue();
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
