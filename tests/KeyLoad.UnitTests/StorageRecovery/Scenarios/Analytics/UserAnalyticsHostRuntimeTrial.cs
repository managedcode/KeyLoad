using KeyLoad.Server;
using P = KeyLoad.UnitTests.StorageRecovery.UserAnalyticsHostProtocol;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class UserAnalyticsHostRuntimeTrial
{
    internal static async Task RunAsync(PartitionHostRecoveryFixture fixture, PartitionHost host,
        Func<UserAnalyticsHostBoundRuntime, Task> body)
    {
        UserAnalyticsRuntime? original = null;
        UserAnalyticsHostBoundWork? originalWork = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var runtime = original = new UserAnalyticsRuntime(ServerRuntimeTestOptions.Runtime(fixture.Options).Core.DatabaseLimits);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                runtime.Open(host);
                await using var work = originalWork = await UserAnalyticsHostBoundWork.OpenAsync(UnitRoutingOptions.Routing(), CancellationToken.None);
                await body(new(runtime, work));
                var closing = runtime.DisposeAsync().AsTask();
                await closing;
                await Assert.That(runtime.DisposeAsync().AsTask()).IsSameReferenceAs(closing);
                await UserAnalyticsHostAssertions.EmptyAsync(host, runtime);
            }, failures);
        }, failures);
        if (original is not { CanReleasePhysical: true } || originalWork is not { IsJoined: true })
        {
            fixture.RetainRoot();
        }

        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task AggregateAsync(PartitionHost host, UserAnalyticsHostBoundRuntime bound,
        bool updated, CancellationToken token)
    {
        var cut = UserAnalyticsHostAssertions.Capture(host, [], []);
        await bound.Work.AggregateAsync(host, bound.Runtime, P.Query(),
            result => UserAnalyticsHostAssertions.SameAsync(P.Expected(updated), result), token);
        await UserAnalyticsHostAssertions.CutAsync(host, cut);
        await UserAnalyticsHostAssertions.EmptyAsync(host, bound.Runtime);
    }

    internal static async Task CancelledAsync(PartitionHost host, UserAnalyticsHostBoundRuntime bound, CancellationToken token)
    {
        var cut = UserAnalyticsHostAssertions.Capture(host, [], []);
        await Assert.ThrowsAsync<OperationCanceledException>(() => bound.Work.AggregateAsync(host,
            bound.Runtime, P.Query(), _ => Task.CompletedTask, token, cancelBefore: true));
        await Assert.That(token.IsCancellationRequested).IsFalse();
        await UserAnalyticsHostAssertions.CutAsync(host, cut);
        await UserAnalyticsHostAssertions.EmptyAsync(host, bound.Runtime);
    }
}
