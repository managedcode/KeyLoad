using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceOwnerInventoryNativeRuntime : IAsyncDisposable
{
    private readonly NativeRequestWorkOwner work;
    private readonly ResourceOwnerInventoryReceiver receiver;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;
    private readonly ResourceOwnerInventoryNativeClient client;
    private readonly Lock closing = new();
    private Task? shutdown;

    internal ResourceOwnerInventoryNativeRuntime(IOptions<GrainRoutingOptions> routing,
        ResourceOwnerInventoryReceiver receiver, ResourceOwnerInventoryNativeClient client, TimeProvider clock)
    {
        this.routing = routing.Value;
        this.receiver = receiver;
        this.client = client;
        this.clock = clock;
        work = new(routing);
    }

    internal async Task<ResourceOwnerInventoryNativeReply> ReadAsync(ResourceOwnerInventoryNativeRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? linked = null;
        ResourceOwnerInventoryNativeReply? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lease = work.Acquire(request.RequestId, NativeRequestWorkKind.ReadCapability);
            var remaining = request.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                result = await receiver.ReadAsync(request, token).ConfigureAwait(false);
                return;
            }
            deadline = new(remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime, clock);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, work.ShutdownToken, deadline.Token);
            result = await receiver.ReadAsync(request, linked.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => deadline?.Dispose(), failures);
        ServerFailureObserver.Observe(() => lease?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
    }

    internal ResourceOwnerInventoryNativeClient Client => client;
    internal bool IsJoined => work.IsJoined && client.IsJoined;

    internal async Task CloseAdmissionAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(work.DrainAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(client.CloseAdmissionAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync()
    {
        lock (closing)
        {
            return new(shutdown ??= StopCoreAsync());
        }
    }

    private async Task StopCoreAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(CloseAdmissionAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => client.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        try
        {
            await work.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
