using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropHeldPersistObservation
{
    internal static async Task<ReplicatedOperation> JoinExpiredAsync(NativeDropPageAttemptState state,
        Task pending, NativeDropCommandPhaseGate held, ResourceRetirementAttemptV1 attempt)
    {
        var expected = new NativeDropExpectedOriginal(pending);
        state.ExpectedOriginals.Add(expected);
        _ = await Task.WhenAny(held.Arrived.Task, pending).WaitAsync(state.Context.Token).ConfigureAwait(false);
        if (!held.Arrived.Task.IsCompletedSuccessfully)
        {
            await pending.ConfigureAwait(false);
            throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingHold);
        }
        var original = await held.Original.Task.WaitAsync(state.Context.Token).ConfigureAwait(false);
        var before = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
        await NativeDropPageRecoveryPrepare.ExpireAsync(state, attempt).ConfigureAwait(false);
        var errors = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => pending, errors).ConfigureAwait(false);
        if (errors.Count == NativeDropPageRecoveryData.ExpectedOriginalFailureCount)
        {
            expected.ExpectedFailure = errors.Single();
        }

        await Assert.That(errors.Count).IsEqualTo(NativeDropPageRecoveryData.ExpectedOriginalFailureCount);
        await Assert.That(NativeCqrsBoundaryErrors.IsNonFatal(expected.ExpectedFailure!)).IsTrue();
        await Assert.That(held.SelectedCancellation.IsCancellationRequested).IsTrue();
        await held.Disposed.Task.WaitAsync(state.Context.Token).ConfigureAwait(false);
        await NativeDropPageRecoveryImages.SameAsync(state.Profile, before, state.Context.Token).ConfigureAwait(false);
        return original;
    }

    internal static async Task WaitPublicAsync(NativeDropPageAttemptState state, NativeDropCommandPhaseGate hold)
    {
        _ = await Task.WhenAny(hold.Arrived.Task, state.Call.Original).WaitAsync(state.Context.Token).ConfigureAwait(false);
        if (hold.Arrived.Task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = NativeDropRuntimeAssertions.Value<DropTableResult>(await state.Call.Original.ConfigureAwait(false));
        throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingHold);
    }
}
