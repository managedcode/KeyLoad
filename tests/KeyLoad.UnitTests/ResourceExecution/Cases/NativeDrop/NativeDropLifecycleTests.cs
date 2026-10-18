namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class NativeDropLifecycleTests
{
    [Test]
    public Task AtomicDropReturnsOriginalReceiptAndReplaysWithoutEffectsAcrossTwoJoinedColdReopens()
        => NativeDropOperationFlow.RunAsync(ExecuteAsync);

    private static async Task ExecuteAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var call = NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, token);
        var result = NativeDropRuntimeAssertions.Value<DropTableResult>(await call.Original.ConfigureAwait(false));
        await NativeDropRuntimeAssertions.DroppedAsync(profile, id, NativeDropRuntimeProtocol.Request, result, token).ConfigureAwait(false);
        var original = await NativeDropRuntimeAssertions.StoredDropAsync(profile, id,
            NativeDropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
        await NativeDropOperationFlow.ReplayAsync(profile, id, result, original, token).ConfigureAwait(false);
        await NativeDropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        await NativeDropOperationFlow.ColdReplayAsync(profile, id, result, original, token).ConfigureAwait(false);
    }
}
