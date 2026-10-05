using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Joins the actual authority-fault scenario and preserves bounded stage evidence.</summary>
internal static class RequestCqrsAuthorityFaultLifecycleRunner
{
    internal static async Task RunAsync(bool officialMcp, CancellationToken cancellationToken)
    {
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        parent.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        var lifecycle = new RequestCqrsLifecycleEvidence();
        var scenario = new RequestCqrsAuthorityFaultScenario(officialMcp, lifecycle, cancellationToken);
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteObservedAsync(parent.Token),
            scenario.Failures).ConfigureAwait(false);
        if (scenario.Failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(
                () => RequestCqrsAuthorityFaultLifecycleAssertions.VerifyRevocationStagesAsync(lifecycle.Snapshot()),
                scenario.Failures).ConfigureAwait(false);
        }
        lifecycle.RecordFirstFailureIfAny(scenario.Failures);
        await ServerFailureObserver.ObserveAsync(scenario.CleanupAsync, scenario.Failures).ConfigureAwait(false);
        lifecycle.RecordTerminal();
        lifecycle.ThrowWithContext(scenario.Failures);
    }
}
