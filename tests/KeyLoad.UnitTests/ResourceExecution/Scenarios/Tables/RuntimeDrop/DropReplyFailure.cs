using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class DropReplyFailure
{
    internal static async Task ExecuteAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var failure = new IOException(DropRuntimeProtocol.ReplyFailure);
        var hold = profile.Observer.Hold(id, GrainRequestPhase.SubmitReturned, failure);
        var call = DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, token);
        try
        {
            await DropOperationFlow.WaitAsync(hold, call, token).ConfigureAwait(false);
            await Assert.That(call.Original.IsCompleted).IsFalse();
            var original = await DropRuntimeAssertions.StoredDropAsync(profile, id,
                DropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
            var local = OrleansSerialization.Deserialize<StoredOutcome>(original).Result.Get<DropTableResult>();
            var expected = local with { Receipt = local.Receipt with { PersistedCopies = DropRuntimeProtocol.PersistedCopies } };
            await DropRuntimeAssertions.DroppedAsync(profile, id, DropRuntimeProtocol.Request, expected, token).ConfigureAwait(false);
            var committed = await DropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
            hold.Released.TrySetResult();
            var reply = await call.Original.ConfigureAwait(false);
            await Assert.That(reply.Error).IsEqualTo((ErrorCode?)ErrorCode.UnknownWriteOutcome);
            await Assert.That(reply.SafeDetail).IsEqualTo("The write outcome is unknown. Retry the same command ID.");
            await Assert.That(reply.Payload.IsEmpty).IsTrue();
            await hold.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
            await DropRuntimeAssertions.SameImageAsync(profile, committed, token).ConfigureAwait(false);
            await DropOperationFlow.ReplayAsync(profile, id, expected, original, token).ConfigureAwait(false);
            await DropOperationFlow.ColdReplayAsync(profile, id, expected, original, token).ConfigureAwait(false);
        }
        finally
        {
            hold.Released.TrySetResult();
        }
    }
}
