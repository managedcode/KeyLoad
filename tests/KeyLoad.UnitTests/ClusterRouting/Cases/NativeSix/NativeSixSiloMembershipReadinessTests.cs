namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises actual six-silo membership refusal, repair and a restarted native generation.</summary>
internal sealed class NativeSixSiloMembershipReadinessTests
{
    /// <summary>Preserves current full membership across invalid names, cancellation and genuine restart.</summary>
    [Test]
    public async Task ActualSixNativeSilosRefuseWrongNamesAndCancellationThenRepairAndFenceRestartedGeneration()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new NativeSixSiloMembershipReadinessFixture();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => fixture.RunAsync(TestContext.Current!.Execution.CancellationToken), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
