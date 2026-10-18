using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropOperationFlow
{
    internal static async Task RunAsync(Func<NativeDropRuntimeProfile, CancellationToken, Task> operation, DatabaseLimits? limits = null)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var failures = new List<Exception>();
        NativeDropRuntimeProfile? profile = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            profile = await NativeDropRuntimeProfile.CreateAsync(token, limits).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(profile.Node.Timing.RestartCompletionTimeout, TimeProvider.System);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            await NativeDropRuntimeSeed.CreateAsync(profile, lifetime.Token).ConfigureAwait(false);
            await NativeDropRuntimeSeed.CreateAsync(profile, lifetime.Token, NativeDropRuntimeProtocol.OtherTable).ConfigureAwait(false);
            await operation(profile, lifetime.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (profile is not null)
        {
            if (failures.Count != NativeDropRuntimeProtocol.NoFailures)
            {
                profile.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => profile.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task WaitAsync(NativeDropCommandPhaseGate hold, NativeDropPublicCall call, CancellationToken token)
    {
        _ = await Task.WhenAny(hold.Arrived.Task, call.Original).WaitAsync(token).ConfigureAwait(false);
        if (!hold.Arrived.Task.IsCompletedSuccessfully)
        {
            _ = NativeDropRuntimeAssertions.Value<DropTableResult>(await call.Original.ConfigureAwait(false));
            throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingHold);
        }
        var identity = await hold.Arrived.Task.WaitAsync(token).ConfigureAwait(false);
        await Assert.That(identity.RequestId).IsEqualTo(call.RequestId);
        await Assert.That(identity.CommandId).IsEqualTo(call.CommandId);
    }

    internal static async Task ReplayAsync(NativeDropRuntimeProfile profile, Guid id, DropTableResult expected,
        byte[] originalOutcome, CancellationToken token)
    {
        var before = await NativeDropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var replay = NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, token);
        var actual = NativeDropRuntimeAssertions.Value<DropTableResult>(await replay.Original.ConfigureAwait(false));
        await NativeDropRuntimeAssertions.SameAsync(expected, actual).ConfigureAwait(false);
        await NativeDropRuntimeAssertions.DroppedAsync(profile, id, NativeDropRuntimeProtocol.Request, actual, token).ConfigureAwait(false);
        var retained = await NativeDropRuntimeAssertions.StoredDropAsync(profile, id, NativeDropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
        await Assert.That(retained.SequenceEqual(originalOutcome)).IsTrue();
        await NativeDropRuntimeAssertions.SameImageAsync(profile, before, token).ConfigureAwait(false);
    }

    internal static async Task ColdReplayAsync(NativeDropRuntimeProfile profile, Guid id, DropTableResult expected,
        byte[] originalOutcome, CancellationToken token)
    {
        for (var cold = NativeDropRuntimeProtocol.FirstCold; cold < NativeDropRuntimeProtocol.ColdReopens; cold++)
        {
            await profile.RestartAsync(token).ConfigureAwait(false);
            await ReplayAsync(profile, id, expected, originalOutcome, token).ConfigureAwait(false);
            await NativeDropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        }
    }

    internal static async Task RejectedReplayAsync(NativeDropRuntimeProfile profile, Guid id, GrainOperationReply expected,
        byte[] originalOutcome, CancellationToken token)
    {
        var before = await NativeDropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var reply = await NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, token).Original.ConfigureAwait(false);
        await Assert.That(reply.Error).IsEqualTo(expected.Error);
        await Assert.That(reply.SafeDetail).IsEqualTo(expected.SafeDetail);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        var retained = await NativeDropRuntimeAssertions.StoredDropAsync(profile, id, NativeDropRuntimeProtocol.Request,
            ErrorCode.BudgetExceeded, token).ConfigureAwait(false);
        await Assert.That(retained.SequenceEqual(originalOutcome)).IsTrue();
        await NativeDropRuntimeAssertions.SameImageAsync(profile, before, token).ConfigureAwait(false);
    }
}
