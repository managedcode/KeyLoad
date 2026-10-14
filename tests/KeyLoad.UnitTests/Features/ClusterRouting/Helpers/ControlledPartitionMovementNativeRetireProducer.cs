using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeRetireProducer
{
    private const string UnknownDetail = "The write outcome is unknown. Retry the same command ID.";

    internal static Task<GrainOperationReply> Start(ControlledPartitionMovementNativeHosts owners,
        PartitionMoveRequest request, CancellationToken cancellationToken)
        => owners.Source.SubmitAsync(OperationKind.MovePartition, request.MoveId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), cancellationToken);

    internal static async Task<ControlledPartitionMovementNativeRetireCaptured> CaptureAsync(
        Task<ControlledPartitionMovementNativeRetireCaptured> capture, Task<GrainOperationReply> original,
        CancellationToken cancellationToken)
    {
        var first = await Task.WhenAny(capture, original).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (first == original)
        {
            _ = await original.ConfigureAwait(false);
            throw new InvalidOperationException("The original movement finished before its genuine Retire receiver borrow.");
        }
        return await capture.ConfigureAwait(false);
    }

    internal static async Task FailedAsync(Task<GrainOperationReply> original)
    {
        var failure = await Assert.ThrowsAsync<KeyLoadException>(async () => { _ = await original.ConfigureAwait(false); });
        await Assert.That(failure).IsNotNull();
        var actual = failure ?? throw new InvalidOperationException("The original canceled movement failure is absent.");
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(actual.Message).IsEqualTo(UnknownDetail);
        await Assert.That(JsonDefaults.Serialize(actual.ToProblem()).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            Errors.Problem(ErrorCode.UnknownWriteOutcome, UnknownDetail)))).IsTrue();
        await Assert.That(original.IsCompleted).IsTrue();
    }

    internal static async Task ExpiredAsync(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeRetireCaptured captured, CancellationToken cancellationToken)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(captured.Operation);
        var expiry = phase.ReceiverEffectAdmission?.OriginalExpiresAt
            ?? throw new InvalidOperationException("The actual signed Retire expiry is absent.");
        var clock = owners.Source.Clock;
        var remaining = expiry - clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, clock, cancellationToken).ConfigureAwait(false);
        }
        await Assert.That(clock.GetUtcNow() >= expiry).IsTrue();
    }

    internal static async Task CompleteAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMoveRequest request, PartitionMoveResult expected, CancellationToken cancellationToken)
    {
        var reply = await Start(owners, request, cancellationToken).ConfigureAwait(false);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMoveResult;
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
