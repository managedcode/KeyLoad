using System.Collections.Immutable;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Owns bounded authenticated metadata callbacks until their signed terminal reply and joined shutdown.</summary>
public sealed class ReplicaNativeDiscoveryRuntime : IAsyncDisposable
{
    private const int ImmediateAdmissionMilliseconds = 0;
    private const int AdmissionOpen = 0;
    private const int AdmissionDisposed = 1;
    private int admissionDisposed;
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaSiloDiscoveryState discovery;
    private readonly ReplicaEnvelopeAuthenticator authentication;
    private readonly ReplicaNativeDiscoveryMembership membership;
    private readonly ReplicaNativeDiscoveryReplay replay;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim admission;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly IReplicaNativeDiscoveryObserver? observer;

    /// <summary>Creates the silo-owned native discovery admission and shutdown owner.</summary>
    /// <param name="configuration">The local fixed voter cohort and RPC deadline.</param>
    /// <param name="discovery">Borrowed actual initialized silo generation and capability state.</param>
    /// <param name="authentication">Borrowed group authentication; this owner never disposes its secret.</param>
    /// <param name="oracle">The actual local Orleans membership oracle.</param>
    /// <param name="options">Centrally validated membership admission, inventory and replay bounds.</param>
    /// <param name="clock">The borrowed clock used for signed expiry and RPC cancellation.</param>
    /// <param name="observer">Optional bounded native metadata transformation after authentication.</param>
    public ReplicaNativeDiscoveryRuntime(IOptions<ReplicaConfiguration> configuration,
        ReplicaSiloDiscoveryState discovery, ReplicaEnvelopeAuthenticator authentication,
        ISiloStatusOracle oracle, IOptions<OrleansMembershipOptions> options, TimeProvider clock,
        IReplicaNativeDiscoveryObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Value);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value);
        ArgumentNullException.ThrowIfNull(clock);
        options.Value.Validate();
        this.configuration = configuration.Value;
        this.discovery = discovery;
        this.authentication = authentication;
        this.clock = clock;
        this.observer = observer;
        membership = new(oracle, options);
        replay = new(options, clock);
        admission = new(options.Value.MaximumAdmissions, options.Value.MaximumAdmissions);
        var stopping = new CancellationTokenSource();
        lifetime = new(stopping, () =>
        {
            DisposeAdmission();
            stopping.Dispose();
        });
    }

    /// <summary>Returns only exact native metadata, with typed failures and no consensus or storage effects.</summary>
    /// <param name="request">The freshly signed caller and addressed full silo generations.</param>
    /// <param name="cancellationToken">Caller cancellation propagated to the admitted native callback.</param>
    /// <returns>The original task containing the signed success or bounded typed error.</returns>
    public Task<ReplicaNativeDiscoveryReply> DiscoverAsync(ReplicaNativeDiscoveryRequest request,
        CancellationToken cancellationToken)
        => ExecuteAsync(request, cancellationToken);

    /// <summary>Reads a bounded current native membership view independently of database readiness.</summary>
    /// <returns>The immutable actual Active silo names and complete native generation addresses.</returns>
    public ImmutableArray<ReplicaNativeMembershipEntry> ReadActiveMembership()
    {
        using var operation = lifetime.TryEnter()
            ?? throw new ObjectDisposedException(nameof(ReplicaNativeDiscoveryRuntime));
        var admitted = false;
        try
        {
            operation.ShutdownToken.ThrowIfCancellationRequested();
            admitted = admission.WaitAsync(ImmediateAdmissionMilliseconds, operation.ShutdownToken).GetAwaiter().GetResult();
            if (!admitted)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery); }
            var local = SiloAddress.FromParsableString(discovery.RuntimeAddress);
            if (!membership.IsCurrent(configuration.LocalId, local))
            { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery); }
            var active = membership.ReadActive();
            operation.ShutdownToken.ThrowIfCancellationRequested();
            if (!membership.IsCurrent(configuration.LocalId, local))
            { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery); }
            return active;
        }
        finally
        {
            if (admitted)
            { admission.Release(); }
        }
    }

    /// <summary>Closes admission, cancels admitted callbacks and joins them before releasing owned resources.</summary>
    /// <returns>The same idempotent original shutdown task for lifecycle and exceptional host disposal.</returns>
    public Task StopAsync() => lifetime.StopAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await lifetime.DisposeAsync().ConfigureAwait(false);
        DisposeAdmission();
    }

    private void DisposeAdmission()
    {
        if (Interlocked.Exchange(ref admissionDisposed, AdmissionDisposed) == AdmissionOpen)
        { admission.Dispose(); }
    }

    private async Task<ReplicaNativeDiscoveryReply> ExecuteAsync(ReplicaNativeDiscoveryRequest request, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = lifetime.TryEnter()
            ?? throw new ObjectDisposedException(nameof(ReplicaNativeDiscoveryRuntime));
        var admitted = false;
        try
        {
            caller.ThrowIfCancellationRequested();
            operation.ShutdownToken.ThrowIfCancellationRequested();
            admitted = await admission.WaitAsync(ImmediateAdmissionMilliseconds, caller).ConfigureAwait(false);
            if (!admitted)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery); }
            authentication.VerifyNativeDiscoveryRequest(request);
            RequireCurrent(request);
            replay.Admit(request);
            var remaining = request.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery); }
            var bound = remaining < configuration.RpcTimeout ? remaining : configuration.RpcTimeout;
            using var timeout = new CancellationTokenSource(bound, clock);
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken, timeout.Token);
            bounded.Token.ThrowIfCancellationRequested();
            var state = discovery.Read();
            if (!state.TransportReady)
            { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery); }
            ReadOnlyMemory<byte> payload = NativeSerialization.Serialize(state);
            ReplicaEnvelopeValidation.Discovery(payload.Span, request.Nonce);
            if (observer is not null)
            { payload = await observer.TransformNativeDiscoveryAsync(request, state, payload, bounded.Token).ConfigureAwait(false); }
            bounded.Token.ThrowIfCancellationRequested();
            RequireCurrent(request);
            if (clock.GetUtcNow() >= request.ExpiresAt || !discovery.Read().TransportReady)
            { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery); }
            ReplicaEnvelopeValidation.Discovery(payload.Span, request.Nonce);
            payload = payload.ToArray();
            return authentication.CreateNativeDiscoveryReply(request, payload);
        }
        catch (KeyLoadException error)
        {
            return authentication.CreateNativeDiscoveryReply(request, ReadOnlyMemory<byte>.Empty, error.Code);
        }
        catch (OperationCanceledException)
        {
            return authentication.CreateNativeDiscoveryReply(request, ReadOnlyMemory<byte>.Empty, ErrorCode.OwnershipLost);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return authentication.CreateNativeDiscoveryReply(request, ReadOnlyMemory<byte>.Empty, ErrorCode.OwnershipLost);
        }
        finally
        {
            if (admitted)
            { admission.Release(); }
        }
    }

    private void RequireCurrent(ReplicaNativeDiscoveryRequest request)
    {
        if (!configuration.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || request.DestinationVoter != configuration.LocalId
            || request.DestinationSiloAddress != discovery.RuntimeAddress)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery); }
        SiloAddress caller;
        SiloAddress destination;
        try
        {
            caller = SiloAddress.FromParsableString(request.CallerSiloAddress);
            destination = SiloAddress.FromParsableString(request.DestinationSiloAddress);
        }
        catch (FormatException)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery); }
        if (caller.ToParsableString() != request.CallerSiloAddress
            || destination.ToParsableString() != request.DestinationSiloAddress
            || !membership.IsCurrent(request.CallerVoter, caller)
            || !membership.IsCurrent(configuration.LocalId, destination))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery); }
    }
}
