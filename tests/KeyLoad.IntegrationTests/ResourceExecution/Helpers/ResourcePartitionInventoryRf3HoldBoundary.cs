using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3HoldBoundary
{
    private const string Missing = "The original inventory completed before its native owner-validation hold.";

    internal static async Task<RequestCqrsProbeMarkerRecord> WaitAsync(RequestCqrsProbeFixture controls,
        Guid arm, IReadOnlyList<KeyLoad.Clustering.ReplicaSiloDiscovery> owners,
        Task<ResourcePartitionInventoryRf3Reply> original, CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var marker = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ResourceOwnerInventoryOwnerValidation,
            RequestCqrsProbeOutcome.Observed, owners, waiting.Token);
        if (await Task.WhenAny(marker, original).ConfigureAwait(false) == original)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
                _ = await ResourcePartitionInventoryRf3Assertions.SuccessAsync(await original.ConfigureAwait(false)), failures);
            await ServerFailureObserver.ObserveAsync(waiting.CancelAsync, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    _ = await marker.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException(Missing);
        }
        var held = await marker.ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(held, arm, Guid.Empty,
            RequestCqrsProbePhase.ResourceOwnerInventoryOwnerValidation, owners);
        await Assert.That(original.IsCompleted).IsFalse();
        return held;
    }
}
