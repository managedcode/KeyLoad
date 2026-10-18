using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropReplyFailure
{
    internal static async Task ExecuteAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var failure = new IOException(NativeDropRuntimeProtocol.ReplyFailure);
        var hold = profile.Observer.Hold(id, GrainRequestPhase.SubmitReturned, failure);
        var call = NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, token);
        try
        {
            await NativeDropOperationFlow.WaitAsync(hold, call, token).ConfigureAwait(false);
            await Assert.That(call.Original.IsCompleted).IsFalse();
            var original = await NativeDropRuntimeAssertions.StoredDropAsync(profile, id,
                NativeDropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
            var local = NativeSerialization.Deserialize<StoredOutcome>(original).Result.Get<DropTableResult>();
            var expected = local with { Receipt = local.Receipt with { PersistedCopies = NativeDropRuntimeProtocol.PersistedCopies } };
            await NativeDropRuntimeAssertions.DroppedAsync(profile, id, NativeDropRuntimeProtocol.Request, expected, token).ConfigureAwait(false);
            var committed = await NativeDropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
            hold.Released.TrySetResult();
            var reply = await call.Original.ConfigureAwait(false);
            await Assert.That(reply.Error).IsEqualTo((ErrorCode?)ErrorCode.UnknownWriteOutcome);
            await Assert.That(reply.SafeDetail).IsEqualTo("The write outcome is unknown. Retry the same command ID.");
            await Assert.That(reply.Payload.IsEmpty).IsTrue();
            await hold.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
            await NativeDropRuntimeAssertions.SameImageAsync(profile, committed, token).ConfigureAwait(false);
            await NativeDropOperationFlow.ReplayAsync(profile, id, expected, original, token).ConfigureAwait(false);
            await NativeDropOperationFlow.ColdReplayAsync(profile, id, expected, original, token).ConfigureAwait(false);
        }
        finally
        {
            hold.Released.TrySetResult();
        }
    }
}
