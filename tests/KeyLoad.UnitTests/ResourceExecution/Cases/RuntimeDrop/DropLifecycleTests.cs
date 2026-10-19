namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class DropLifecycleTests
{
    [Test]
    public Task AtomicDropReturnsOriginalReceiptAndReplaysWithoutEffectsAcrossTwoJoinedColdReopens()
        => DropOperationFlow.RunAsync(ExecuteAsync);

    private static async Task ExecuteAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var call = DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, token);
        var result = DropRuntimeAssertions.Value<DropTableResult>(await call.Original.ConfigureAwait(false));
        await DropRuntimeAssertions.DroppedAsync(profile, id, DropRuntimeProtocol.Request, result, token).ConfigureAwait(false);
        var original = await DropRuntimeAssertions.StoredDropAsync(profile, id,
            DropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
        await DropOperationFlow.ReplayAsync(profile, id, result, original, token).ConfigureAwait(false);
        await DropRuntimeSeed.HealthyOtherAsync(profile, token).ConfigureAwait(false);
        await DropOperationFlow.ColdReplayAsync(profile, id, result, original, token).ConfigureAwait(false);
    }
}
