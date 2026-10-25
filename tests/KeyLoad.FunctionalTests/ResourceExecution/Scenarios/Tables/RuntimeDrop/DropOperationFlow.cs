using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class DropOperationFlow
{
    internal static async Task RunAsync(Func<DropRuntimeProfile, CancellationToken, Task> operation, DatabaseLimits? limits = null)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var failures = new List<Exception>();
        DropRuntimeProfile? profile = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            profile = await DropRuntimeProfile.CreateAsync(token, limits).ConfigureAwait(false);
            await DropRuntimeSeed.CreateAsync(profile, token).ConfigureAwait(false);
            await DropRuntimeSeed.CreateAsync(profile, token, DropRuntimeProtocol.OtherTable).ConfigureAwait(false);
            await operation(profile, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (profile is not null)
        {
            if (failures.Count != DropRuntimeProtocol.NoFailures)
            {
                profile.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => profile.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task WaitAsync(DropCommandPhaseGate hold, DropPublicCall call, CancellationToken token)
    {
        _ = await Task.WhenAny(hold.Arrived.Task, call.Original).WaitAsync(token).ConfigureAwait(false);
        if (!hold.Arrived.Task.IsCompletedSuccessfully)
        {
            _ = DropRuntimeAssertions.Value<DropTableResult>(await call.Original.ConfigureAwait(false));
            throw new InvalidOperationException(DropRuntimeProtocol.MissingHold);
        }
        var identity = await hold.Arrived.Task.WaitAsync(token).ConfigureAwait(false);
        await Assert.That(identity.RequestId).IsEqualTo(call.RequestId);
        await Assert.That(identity.CommandId).IsEqualTo(call.CommandId);
    }

    internal static async Task ReplayAsync(DropRuntimeProfile profile, Guid id, DropTableResult expected,
        byte[] originalOutcome, CancellationToken token)
    {
        var before = await DropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var replay = DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, token);
        var actual = DropRuntimeAssertions.Value<DropTableResult>(await replay.Original.ConfigureAwait(false));
        await DropRuntimeAssertions.SameAsync(expected, actual).ConfigureAwait(false);
        await DropRuntimeAssertions.DroppedAsync(profile, id, DropRuntimeProtocol.Request, actual, token).ConfigureAwait(false);
        var retained = await DropRuntimeAssertions.StoredDropAsync(profile, id, DropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
        await Assert.That(retained.SequenceEqual(originalOutcome)).IsTrue();
        await DropRuntimeAssertions.SameImageAsync(profile, before, token).ConfigureAwait(false);
    }

    internal static async Task ColdReplayAsync(DropRuntimeProfile profile, Guid id, DropTableResult expected,
        byte[] originalOutcome, CancellationToken token)
    {
        for (var cold = DropRuntimeProtocol.FirstCold; cold < DropRuntimeProtocol.ColdReopens; cold++)
        {
            await profile.RestartAsync(token).ConfigureAwait(false);
            await ReplayAsync(profile, id, expected, originalOutcome, token).ConfigureAwait(false);
            await DropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        }
    }

    internal static async Task RejectedReplayAsync(DropRuntimeProfile profile, Guid id, GrainOperationReply expected,
        byte[] originalOutcome, CancellationToken token)
    {
        var before = await DropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var reply = await DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, token).Original.ConfigureAwait(false);
        await Assert.That(reply.Error).IsEqualTo(expected.Error);
        await Assert.That(reply.SafeDetail).IsEqualTo(expected.SafeDetail);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        var retained = await DropRuntimeAssertions.StoredDropAsync(profile, id, DropRuntimeProtocol.Request,
            ErrorCode.BudgetExceeded, token).ConfigureAwait(false);
        await Assert.That(retained.SequenceEqual(originalOutcome)).IsTrue();
        await DropRuntimeAssertions.SameImageAsync(profile, before, token).ConfigureAwait(false);
    }
}
