using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3BackpressureCleanup
{
    internal static async Task RunAsync(RequestCqrsProbeFixture controls, Guid arm,
        ReplicaSiloDiscovery[] discovery, PostgresRf3WireOwner wire, TaskCompletionSource resume,
        Task header, Task original, bool joined,
        List<Exception> failures)
    {
        resume.TrySetResult();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                if (failures.Count != PostgresRf3Protocol.FirstColumn)
                {
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => wire.DisposeAsync().AsTask(), failures);
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures);
                }
                if (!joined)
                { await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => original.WaitAsync(cleanup.Token), failures); }
                if (header.IsFaulted)
                { _ = header.Exception; }
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    _ = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
                        RequestCqrsProbeOutcome.Observed, discovery, cleanup.Token);
                    await controls.RetireArmAsync(arm, cleanup.Token);
                }, failures);
            }, failures);
        }, failures);
    }
}
