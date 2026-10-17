using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed partial class ResourceOwnerInventoryNativeRuntime : IAsyncDisposable
{
    private readonly NativeRequestWorkOwner work;
    private readonly ResourceOwnerInventoryReceiver receiver;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;
    private readonly ResourceOwnerInventoryNativeClient client;
    private readonly Lock closing = new();
    private Task? shutdown;

    internal ResourceOwnerInventoryNativeRuntime(IOptions<GrainRoutingOptions> routing,
        ResourceOwnerInventoryReceiver receiver, ResourceRetirementReceiver retirement,
        ResourceOwnerInventoryNativeClient client, TimeProvider clock)
    {
        this.routing = routing.Value;
        this.receiver = receiver;
        this.retirement = retirement;
        this.client = client;
        this.clock = clock;
        work = new(routing);
    }

    internal Task<ResourceOwnerInventoryNativeReply> ReadAsync(ResourceOwnerInventoryNativeRequest request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(request.RequestId, request.ExpiresAt, NativeRequestWorkKind.ReadCapability,
            ResourceOwnerInventoryNativeProtocol.Unavailable,
            caller => receiver.ReadAsync(request, caller), token);
    }

    private async Task<T> ExecuteAsync<T>(Guid requestId, DateTimeOffset expiresAt, NativeRequestWorkKind kind, string unavailable,
        Func<CancellationToken, Task<T>> operation, CancellationToken token) where T : class
    {
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? linked = null;
        T? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lease = work.Acquire(requestId, kind);
            var remaining = expiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                result = await operation(token).ConfigureAwait(false);
                return;
            }
            deadline = new(remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime, clock);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, work.ShutdownToken, deadline.Token);
            result = await operation(linked.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => deadline?.Dispose(), failures);
        ServerFailureObserver.Observe(() => lease?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, unavailable);
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
