using KeyLoad.Clustering;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns finite diagnostic admission and joins original native requests before releasing credentials.</summary>
internal sealed class RequestCqrsProbeNativeRelay : IAsyncDisposable
{
    private const int ResourcesNotReleased = 0;
    private const int ResourcesReleased = 1;
    private int resourcesReleased;
    private readonly RequestCqrsProbeNativeTrust trust;
    private readonly RequestCqrsProbeNativeAuthentication authentication;
    private readonly RequestCqrsProbeNativeTransport transport;
    private readonly ReplicaNativeDiscoveryReplay replay;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim incoming;
    private readonly SemaphoreSlim outgoing;
    private readonly ReplicaConfiguration configuration;
    private readonly TimeProvider clock;

    public RequestCqrsProbeNativeRelay(IServiceProvider services, IOptions<NodeOptions> node,
        IOptions<ReplicaConfiguration> configuration, ILocalSiloDetails local, ISiloStatusOracle oracle,
        IOptions<OrleansMembershipOptions> membership, IOptions<RequestProbeExecutionOptions> probe, TimeProvider clock)
    {
        this.configuration = configuration.Value;
        this.clock = clock;
        trust = new(node, configuration, local, oracle, membership);
        authentication = new(trust, configuration, membership, probe, clock);
        transport = new(trust, authentication, new(services));
        replay = new(membership, clock);
        incoming = new(membership.Value.MaximumAdmissions, membership.Value.MaximumAdmissions);
        outgoing = new(membership.Value.MaximumAdmissions, membership.Value.MaximumAdmissions);
        lifetime = new(stopping, Release);
    }

    internal async ValueTask NotifyDisposedAsync(RequestCqrsProbeNativeIdentity identity, SiloAddress producerAddress)
    {
        using var operation = Enter();
        if (!string.Equals(producerAddress.ToParsableString(), trust.Address, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable);
        }
        _ = trust.RequireCurrent(trust.Local, trust.Voter, trust.Address);
        if (!await outgoing.WaitAsync(TimeSpan.Zero, operation.ShutdownToken).ConfigureAwait(false))
        {
            throw Capacity();
        }
        try
        {
            using var deadline = new CancellationTokenSource(configuration.RpcTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(operation.ShutdownToken, deadline.Token);
            await transport.NotifyAsync(identity, linked.Token).ConfigureAwait(false);
        }
        finally { outgoing.Release(); }
    }

    internal async Task<RequestCqrsProbeNativeReply> ExecuteAsync(RequestCqrsProbeNativeMethod method,
        RequestCqrsProbeNativeRequest request, RequestCqrsProbeProducerDisposal disposal,
        Action<Exception> observeFailure, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = Enter();
        if (!await incoming.WaitAsync(TimeSpan.Zero, token).ConfigureAwait(false))
        {
            throw Capacity();
        }
        try
        {
            var owned = authentication.Verify(request, method);
            replay.Admit(new(owned.Version, owned.CallerVoter, owned.CallerSiloAddress, owned.DestinationVoter,
                owned.DestinationSiloAddress, owned.Nonce, owned.ExpiresAt, owned.Signature));
            return await DispatchAsync(owned, disposal, observeFailure, token, operation.ShutdownToken).ConfigureAwait(false);
        }
        finally { incoming.Release(); }
    }

    private Task<RequestCqrsProbeNativeReply> DispatchAsync(RequestCqrsProbeNativeRequest request,
        RequestCqrsProbeProducerDisposal disposal, Action<Exception> observeFailure,
        CancellationToken caller, CancellationToken shutdown)
    {
        var dispatched = false;
        try
        {
            var remaining = request.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                throw new OperationCanceledException();
            }
            using var expiry = new CancellationTokenSource(remaining < configuration.RpcTimeout ? remaining : configuration.RpcTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, shutdown, expiry.Token);
            linked.Token.ThrowIfCancellationRequested();
            authentication.RequireCurrent(request);
            var claim = request.Method == RequestCqrsProbeNativeMethod.Locate
                ? disposal.Locate(request.Identity, trust.Local, trust.Voter, trust.Address) : request.Claim;
            if (request.Method == RequestCqrsProbeNativeMethod.Notify)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (request.ExpiresAt <= clock.GetUtcNow())
                {
                    throw new OperationCanceledException();
                }
                authentication.RequireCurrent(request);
                dispatched = true;
                disposal.Complete(claim!, trust.Local, trust.Voter, trust.Address);
            }
            authentication.RequireCurrent(request);
            return Task.FromResult(authentication.Reply(request, claim, dispatched));
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            observeFailure(error);
            var code = dispatched ? ErrorCode.UnknownWriteOutcome
                : error is OperationCanceledException ? ErrorCode.Cancelled : ErrorCode.OwnershipLost;
            return Task.FromResult(authentication.Reply(request, null, false, code));
        }
    }

    internal Task StopAsync() => lifetime.StopAsync();
    public async ValueTask DisposeAsync()
    {
        await lifetime.DisposeAsync().ConfigureAwait(false);
        Release();
    }
    private ReplicaDiscoveryLifetime.Operation Enter()
        => lifetime.TryEnter() ?? throw new ObjectDisposedException(nameof(RequestCqrsProbeNativeRelay));
    private static KeyLoadException Capacity() => Errors.Fail(ErrorCode.ResourceExhausted, RequestCqrsProbeNativeProtocol.Unavailable);
    private void Release()
    {
        if (Interlocked.Exchange(ref resourcesReleased, ResourcesReleased) != ResourcesNotReleased)
        {
            return;
        }
        var failures = new List<Exception>();
        try
        {
            trust.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            incoming.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            outgoing.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
