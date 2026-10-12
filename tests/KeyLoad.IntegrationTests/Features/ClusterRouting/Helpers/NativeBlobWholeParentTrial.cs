using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeParentTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new NativeBlobWholeParentOwner(failures);
            try
            {
                var original = owner.RunAsync(lifetime.Token);
                await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
            }
            finally
            { ServerFailureObserver.Observe(lifetime.Cancel, failures); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
