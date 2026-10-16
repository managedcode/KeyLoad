using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryLifetimeNativeHoldBoundary
{
    private const string Missing = "The original public read completed before its actual native reply observation.";

    internal static async Task<RequestCqrsProbeMarkerRecord> WaitAsync(RequestCqrsProbeFixture controls, Guid arm,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, Task original, Func<Task> observeOriginal,
        CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var marker = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.NativeModelAfterDispatch,
            RequestCqrsProbeOutcome.Observed, discovery, waiting.Token);
        if (await Task.WhenAny(marker, original).ConfigureAwait(false) == original)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(observeOriginal, failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(waiting.CancelAsync, failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    _ = await marker.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException(Missing);
        }
        var observed = await marker.ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(observed, arm, Guid.Empty,
            RequestCqrsProbePhase.NativeModelAfterDispatch, discovery);
        await Assert.That(original.IsCompleted).IsFalse();
        return observed;
    }
}
