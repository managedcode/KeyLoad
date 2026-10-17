using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class NativeDropPreparedPageTests
{
    [Test]
    public async Task RealNativeOwnersSealD3AndD4ThenPreparePersistExecuteCleanupAndReplayAcrossTwoJoinedColdReopens()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var failures = new List<Exception>();
        NativeDropRuntimeProfile? profile = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            profile = await NativeDropRuntimeProfile.CreateAsync(token).ConfigureAwait(false);
            await NativeDropPreparedPageFlow.ExecuteAsync(profile, token).ConfigureAwait(false);
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
}
