using KeyLoad.IntegrationTests.Features.ClusterRouting.Assertions;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Joins the actual authority-fault scenario and preserves bounded stage evidence.</summary>
internal static class RequestCqrsAuthorityFaultLifecycleRunner
{
    internal static async Task RunAsync(bool officialMcp, CancellationToken cancellationToken)
    {
        using var parentTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, parentTimeout.Token);
        var lifecycle = new RequestCqrsLifecycleEvidence();
        var scenario = new RequestCqrsAuthorityFaultScenario(officialMcp, lifecycle, TimeProvider.System, cancellationToken);
        await ServerFailureObserver.ObserveAsync(() => ExecuteObservedAsync(scenario, lifecycle, parent.Token),
            scenario.Failures).ConfigureAwait(false);
        if (scenario.Failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(
                () => RequestCqrsAuthorityFaultLifecycleAssertions.VerifyRevocationStagesAsync(lifecycle.Snapshot()),
                scenario.Failures).ConfigureAwait(false);
        }
        lifecycle.RecordFirstFailureIfAny(scenario.Failures);
        var cleanup = scenario.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => cleanup, scenario.Failures).ConfigureAwait(false);
        lifecycle.RecordTerminal();
        lifecycle.ThrowWithContext(scenario.Failures);
    }

    private static async Task ExecuteObservedAsync(RequestCqrsAuthorityFaultScenario scenario,
        RequestCqrsLifecycleEvidence lifecycle, CancellationToken parentToken)
    {
        try
        { await scenario.ExecuteAsync(parentToken).ConfigureAwait(false); }
        catch (Exception)
        {
            lifecycle.RecordFirstFailure();
            throw;
        }
    }
}
