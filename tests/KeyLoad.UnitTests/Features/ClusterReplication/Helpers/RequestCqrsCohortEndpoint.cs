using System.Collections.Concurrent;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortEndpoint : IReplicaNativeDiscoveryObserver
{
    private const int MaximumControlledRequests = 8;
    private readonly ConcurrentDictionary<Guid, byte> requests = new();
    private volatile ReplicaSiloDiscovery? selected;
    private volatile RequestCqrsCohortNativeGate? dispatchGate;
    private volatile RequestCqrsCohortNativeGate? payloadGate;
    private volatile bool unavailable;
    private volatile bool malformed;
    private long count;
    internal ReplicaNativeDiscoveryRuntime? Runtime
    {
        get; set;
    }
    internal bool Stopped
    {
        get; private set;
    }
    internal long Requests => Interlocked.Read(ref count);

    internal void BeforeSend(ReplicaNativeDiscoveryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (requests.Count >= MaximumControlledRequests || !requests.TryAdd(request.Nonce, 0))
        {
            throw new InvalidOperationException("Native controlled discovery admission exceeded its bound.");
        }
    }
    internal async ValueTask BeforeSendAsync(CancellationToken token)
    {
        if (dispatchGate is { } gate)
        {
            await gate.WaitAsync(token);
        }

        token.ThrowIfCancellationRequested();
    }
    public async ValueTask<ReadOnlyMemory<byte>> TransformNativeDiscoveryAsync(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery actual, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        if (requests.ContainsKey(request.Nonce) && payloadGate is { } gate)
        {
            await gate.WaitAsync(token);
        }

        token.ThrowIfCancellationRequested();
        return TransformNativeDiscovery(request, actual, payload, token);
    }
    public ReadOnlyMemory<byte> TransformNativeDiscovery(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery actual, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        // Only nonce-enrolled scenario requests are transformed; actual consensus discovery remains untouched.
        if (!requests.TryRemove(request.Nonce, out _))
        {
            return payload;
        }

        Interlocked.Increment(ref count);
        token.ThrowIfCancellationRequested();
        if (unavailable)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var result = selected is { } state ? NativeSerialization.Serialize(state) : payload.ToArray();
        return result;
    }
    internal ReplicaNativeDiscoveryReply Receive(ReplicaNativeDiscoveryReply reply, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!malformed || reply.Error is not null)
        {
            return reply;
        }

        var copy = reply.Payload.ToArray();
        if (copy.Length == 0)
        {
            throw new InvalidOperationException("The genuine reply payload is empty.");
        }

        copy[0] ^= 1;
        return reply with
        {
            Payload = copy
        };
    }
    internal void SetDiscovery(ReplicaEnvelopeAuthenticator authentication, ReplicaSiloDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        selected = discovery;
        unavailable = false;
        malformed = false;
        ReleaseAll();
        dispatchGate = null;
        payloadGate = null;
    }
    internal void SetTamperedPayload() => malformed = true;
    internal void SetHeaderGate(RequestCqrsCohortNativeGate gate) => dispatchGate = gate;
    internal void SetBodyGate(RequestCqrsCohortNativeGate gate) => payloadGate = gate;
    internal void SetUnavailable()
    {
        unavailable = true;
        ReleaseAll();
        dispatchGate = null;
        payloadGate = null;
    }
    internal async Task StopListeningAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await (Runtime ?? throw new InvalidOperationException("The actual native runtime is absent.")).StopAsync();
        Stopped = true;
        token.ThrowIfCancellationRequested();
    }
    internal void ReleaseAll()
    {
        dispatchGate?.Release();
        payloadGate?.Release();
    }
    internal void Reset()
    {
        ReleaseAll();
        selected = null;
        dispatchGate = null;
        payloadGate = null;
        unavailable = false;
        malformed = false;
        requests.Clear();
        Interlocked.Exchange(ref count, 0);
    }
}

internal sealed class RequestCqrsCohortNativeGate
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource aborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Entered => entered.Task;
    internal Task Aborted => aborted.Task;
    internal async Task WaitAsync(CancellationToken token)
    {
        entered.TrySetResult();
        try
        {
            await released.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { aborted.TrySetResult(); throw; }
    }
    internal void Release() => released.TrySetResult();
}
