namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises actual six-silo membership refusal, repair and a restarted native generation.</summary>
internal sealed class NativeSixSiloMembershipReadinessTests
{
    /// <summary>Preserves current full membership across invalid names, cancellation and genuine restart.</summary>
    [Test]
    public async Task ActualSixNativeSilosRefuseWrongNamesAndCancellationThenRepairAndFenceRestartedGeneration()
    {
        var fixture = new NativeSixSiloMembershipReadinessFixture();
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => fixture.RunAsync(TestContext.Current!.Execution.CancellationToken), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            fixture.Cluster.StopAllSilosAsync, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            async () => await fixture.Cluster.DisposeAsync().ConfigureAwait(false), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
