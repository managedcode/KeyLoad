using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaMaintenanceCheckpointTrial
{
    internal static async Task RunAsync(bool failNativeFlush, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var observationDeadline = new CancellationTokenSource(
            RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
        using var observed = CancellationTokenSource.CreateLinkedTokenSource(token, observationDeadline.Token);
        var observationToken = observed.Token;
        var scenario = new RequestCqrsCohortCheckpointScenario(failNativeFlush, observationToken);
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        var acquired = false;
        try
        {
            await using var fixture = new RequestCqrsCohortRuntimeFixture(scenario);
            acquired = true;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                await scenario.Entered.WaitAsync(observationToken);
                await ReplicaMaintenanceCheckpointAssertions.RequirePreparedAsync(fixture, scenario);
                var node = fixture.Node(RequestCqrsCohortScenario.LocalVoter);
                await ReplicaMaintenanceCheckpointAssertions.RequirePendingAsync(node.Journal.Consensus, observationToken);
                if (failNativeFlush)
                {
                    scenario.Release();
                    await ReplicaMaintenanceCheckpointAssertions.RequireFaultFenceAsync(node.Journal.Consensus, observationToken);
                    await Assert.That(node.Journal.Log.State.Snapshot).IsEqualTo(
                        scenario.Prepared[RequestCqrsCohortScenario.LocalVoter].Snapshot);
                    var original = new List<Exception>();
                    await ServerFailureObserver.ObserveAsync(() => node.Journal.Consensus.StopAsync(CancellationToken.None), original);
                    await ReplicaMaintenanceCheckpointAssertions.RequireNativeFailuresAsync(original);
                }
                else
                {
                    var original = node.Journal.Consensus.StopAsync(CancellationToken.None);
                    await Assert.That(original.IsCompleted).IsFalse();
                    scenario.Release();
                    await original;
                }
            }, failures);
            ServerFailureObserver.Observe(scenario.Release, failures);
        }
        catch (Exception failure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { (acquired ? cleanup : failures).Add(failure); }
        catch (Exception failure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { (acquired ? cleanup : failures).Add(failure); }
        if (failNativeFlush && failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(() =>
                ReplicaMaintenanceCheckpointAssertions.RequireNativeFailuresAsync(cleanup), failures);
        }
        if (!failNativeFlush || failures.Count != 0)
        { failures.AddRange(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
