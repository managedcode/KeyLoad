using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireProducer
{
    private const string TransportUnavailable = "The write response is unavailable. Retry the same command ID.";
    private const string NativeUnknown = "The write outcome is unknown. Retry the same command ID.";

    internal static async Task FailedAsync(Task<Result<PartitionMoveResult>> original, bool callerCancelled)
    {
        var actual = await original;
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        await Assert.That(actual.Problem).IsNotNull();
        var problem = actual.Problem ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
        var detail = callerCancelled ? TransportUnavailable : problem.Detail switch
        {
            TransportUnavailable => TransportUnavailable,
            NativeUnknown => NativeUnknown,
            PartitionMovePublicProtocol.Interrupted => PartitionMovePublicProtocol.Interrupted,
            _ => throw new InvalidOperationException(ControlledRetireAspireProtocol.InvalidCut)
        };
        await Assert.That(detail is TransportUnavailable or NativeUnknown or PartitionMovePublicProtocol.Interrupted).IsTrue();
        await Assert.That(problem.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(JsonDefaults.Serialize(problem).AsSpan().SequenceEqual(
            JsonDefaults.Serialize(Errors.Problem(ErrorCode.UnknownWriteOutcome, detail)))).IsTrue();
        await Assert.That(original.IsCompleted).IsTrue();
    }

    internal static async Task ExpiredAsync(ControlledRetireAspireOriginal original, CancellationToken token)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(original.Operation);
        var expires = phase.ReceiverEffectAdmission?.OriginalExpiresAt
            ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
        var remaining = expires - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System, token);
        }
        await Assert.That(TimeProvider.System.GetUtcNow() >= expires).IsTrue();
    }

    internal static async Task BeforeExpiryAsync(ControlledRetireAspireOriginal original)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(original.Operation);
        var admission = phase.ReceiverEffectAdmission ?? throw new InvalidOperationException(ControlledRetireAspireProtocol.Missing);
        await Assert.That(original.Phase.OriginalExpiresAt).IsEqualTo(admission.OriginalExpiresAt);
        await Assert.That(TimeProvider.System.GetUtcNow() < admission.OriginalExpiresAt).IsTrue();
    }
}
