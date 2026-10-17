namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RuntimeJournalNativeDataSource]
[NotInParallel]
internal sealed class RuntimeJournalGraphCallerTests(RuntimeJournalNativeFixture fixture)
{
    private const string JournalPrefix = "graph-caller";
    private const int InitialFirstByte = 7;
    private const int InitialSecondByte = 2;
    private const int FollowUpFirstByte = 8;
    private const int FollowUpSecondByte = 3;

    [Test]
    public async Task NativeJournalCallerIsExactRestoredAndHealthyAfterDeniedCalls()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var grainId = $"native/{JournalPrefix}/{Guid.NewGuid():N}";
        var grain = fixture.Cluster.Client.GetGrain<IRuntimeJournalReplayGrain>(grainId);
        var original = new byte[] { InitialFirstByte, InitialSecondByte };
        await grain.SetAsync(original);
        await AssertStoredAsync(grain, original);

        var probe = fixture.Cluster.Client.GetGrain<IRuntimeJournalGraphCallerProbeGrain>(Guid.NewGuid());
        var observation = await probe.ExerciseAsync(cancellationToken);
        await AssertCallerAndDenialOutcomes(observation);
        await AssertStoredAsync(grain, original);

        var followUp = new byte[] { FollowUpFirstByte, FollowUpSecondByte };
        await grain.SetAsync(followUp);
        var priorActivation = await grain.GetActivationTokenAsync();
        await grain.DeactivateAsync();
        await RuntimeJournalGraphCallerSupport.WaitForActivationChangeAsync(grain, priorActivation,
            fixture.TimingOptions.Value);
        var reopened = fixture.Cluster.Client.GetGrain<IRuntimeJournalReplayGrain>(grainId);
        await AssertStoredAsync(reopened, followUp);
        var beforeDeleteActivation = await reopened.GetActivationTokenAsync();
        await reopened.DeleteAsync();
        await reopened.DeactivateAsync();
        await RuntimeJournalGraphCallerSupport.WaitForActivationChangeAsync(reopened,
            beforeDeleteActivation, fixture.TimingOptions.Value);
        await Assert.That(await fixture.Cluster.Client.GetGrain<IRuntimeJournalReplayGrain>(grainId).ReadAsync())
            .IsNull();
    }

    private static async Task AssertStoredAsync(IRuntimeJournalReplayGrain grain, byte[] expected)
    {
        var actual = await grain.ReadAsync();
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.SequenceEqual(expected)).IsTrue();
    }

    private static async Task AssertCallerAndDenialOutcomes(RuntimeJournalGraphCallerResult observation)
    {
        await Assert.That(observation.ProviderReadCompleted).IsTrue();
        await Assert.That(observation.ProviderRestoredScopedCaller).IsTrue();
        await Assert.That(observation.WrongMethodDenied).IsTrue();
        await Assert.That(observation.WrongMethodRestoredScopedCaller).IsTrue();
        await Assert.That(observation.WrongTargetDenied).IsTrue();
        await Assert.That(observation.WrongTargetRestoredScopedCaller).IsTrue();
        await Assert.That(observation.ProbeRestoredOriginalCaller).IsTrue();
    }
}
