using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelNativeRuntime : IAsyncDisposable
{
    private const int ImmediateAdmissionMilliseconds = 0;
    private const string AttachedRuntimeFailure = "The original remote model runtime is already attached.";
    private const string NonceFormat = "N";
    private const string ReplyDeliveryFailure = "The original native model operation failed before its reply was delivered. Effect uncertainty: {Uncertain}.";
    private static readonly Action<ILogger, bool, Exception?> logReplyDeliveryFailure =
        LoggerMessage.Define<bool>(LogLevel.Error, default, ReplyDeliveryFailure);
    private readonly CancellationTokenSource stopping;
    private int resourcesDisposed;
    private readonly PartitionHost partition;
    private readonly NodeOptions options;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;
    private readonly GrainRoutingOptions routing;
    private readonly SiloStorageIdentityProvider local;
    private readonly ReplicaNativeDiscoveryMembership membership;
    private readonly TimeProvider clock;
    private readonly ILogger<RemoteModelNativeRuntime> logger;
    private readonly Func<NativeRequestWorkOwner> requestWork;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly PartitionMovementReplayCache replay;
    private readonly SemaphoreSlim admission;
    private RemoteDocumentRuntime? runtime;

    public RemoteModelNativeRuntime(PartitionHost partition, IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membershipOptions, IOptions<GrainRoutingOptions> routing,
        ISiloStatusOracle oracle, SiloStorageIdentityProvider local, TimeProvider clock, ILogger<RemoteModelNativeRuntime> logger,
        Func<NativeRequestWorkOwner> requestWork)
    {
        this.partition = partition;
        this.options = options.Value;
        this.membershipOptions = membershipOptions;
        this.routing = routing.Value;
        this.local = local;
        this.clock = clock;
        this.logger = logger;
        this.requestWork = requestWork;
        membershipOptions.Value.Validate();
        membership = new(oracle, membershipOptions);
        replay = new(clock, membershipOptions);
        admission = new(membershipOptions.Value.MaximumAdmissions, membershipOptions.Value.MaximumAdmissions);
        stopping = new();
        lifetime = new(stopping, DisposeOwnedResources);
    }

    internal void Attach(RemoteDocumentRuntime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Interlocked.CompareExchange(ref runtime, owner, null) is not null)
        {
            throw new InvalidOperationException(AttachedRuntimeFailure);
        }
    }

    internal async Task<RemoteModelNativeReply> ExchangeAsync(RemoteModelNativeRequest original, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(original);
        using var operation = lifetime.TryEnter() ?? throw new ObjectDisposedException(nameof(RemoteModelNativeRuntime));
        var admitted = false;
        try
        {
            caller.ThrowIfCancellationRequested();
            operation.ShutdownToken.ThrowIfCancellationRequested();
            admitted = await admission.WaitAsync(ImmediateAdmissionMilliseconds, caller).ConfigureAwait(false);
            if (!admitted)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteModelNativeProtocol.Unavailable);
            }
            RemoteModelNativeIdentity.RequireShape(original, membershipOptions);
            var request = original with
            {
                Payload = original.Payload.ToArray()
            };
            RequireCurrent(request);
            using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, request.SourceOwner));
            using var targetMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, request.DestinationOwner));
            if (!sourceMac.VerifyNativeRequest(request))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
            }
            if (NativeLifetimeReadTransportExecution.Handles(request.Channel))
            {
                return await NativeLifetimeReadTransportExecution.ExecuteAsync(requestWork(), partition.Database,
                    request, (binding, boundToken) =>
                        ExecuteAdmittedAsync(request, targetMac, binding, boundToken, operation.ShutdownToken),
                    caller, operation.ShutdownToken)
                    .ConfigureAwait(false);
            }
            return await ExecuteAdmittedAsync(request, targetMac, null, caller, operation.ShutdownToken).ConfigureAwait(false);
        }
        finally
        {
            if (admitted)
            {
                admission.Release();
            }
        }
    }

    private async Task<RemoteModelNativeReply> ExecuteAdmittedAsync(RemoteModelNativeRequest request,
        RemoteDocumentMac targetMac, IResourceLifetimeWorkBinding? binding, CancellationToken caller, CancellationToken shutdown)
    {
        var effectBoundary = new RemoteModelNativeEffectBoundary();
        RemoteModelNativeReply reply;
        try
        {
            RequireCurrent(request);
            if (!replay.TryUse(request.Nonce.ToString(NonceFormat)))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
            }
            var remaining = request.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteModelNativeProtocol.Unavailable);
            }
            var bound = remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime;
            using var deadline = new CancellationTokenSource(bound, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, shutdown, deadline.Token);
            var token = linked.Token;
            token.ThrowIfCancellationRequested();
            var owner = Volatile.Read(ref runtime) ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable);
            RequireCurrent(request);
            token.ThrowIfCancellationRequested();
            var actual = await owner.ExecuteNativeAsync(request, effectBoundary, token, binding).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RequireCurrent(request);
            if (request.ExpiresAt <= clock.GetUtcNow())
            {
                throw new OperationCanceledException(token);
            }
            if (actual.Payload.IsEmpty || actual.Payload.Length > RemoteDocumentProtocol.MaximumBodyBytes
                || !RemoteModelNativeIdentity.Signature(actual.Signature))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteModelNativeProtocol.InvalidProof);
            }
            reply = new(actual.Payload, actual.Signature, null, null, request.Nonce, string.Empty);
        }
        catch (KeyLoadException error)
        {
            reply = Failure(request, effectBoundary.IsStarted ? ErrorCode.UnknownWriteOutcome : error.Code);
        }
        catch (OperationCanceledException)
        {
            reply = Failure(request, effectBoundary.IsStarted ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            logReplyDeliveryFailure(logger, effectBoundary.IsStarted, null);
            reply = Failure(request, effectBoundary.IsStarted ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost);
        }
        return reply with
        {
            SenderSignature = targetMac.SignNativeReply(reply)
        };
    }

    private void RequireCurrent(RemoteModelNativeRequest request)
        => RemoteModelNativeCurrentIdentity.Require(options, partition, local, membership, request);

    private static RemoteModelNativeReply Failure(RemoteModelNativeRequest request, ErrorCode error)
        => new(ReadOnlyMemory<byte>.Empty, string.Empty, error, RemoteModelNativeProtocol.Unavailable, request.Nonce, string.Empty);
    internal Task StopAsync() => lifetime.StopAsync();
    public async ValueTask DisposeAsync()
    {
        try
        {
            await lifetime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            DisposeOwnedResources();
        }
    }

    private void DisposeOwnedResources()
    {
        const int DisposedResources = 1;
        const int LiveResources = 0;
        if (Interlocked.Exchange(ref resourcesDisposed, DisposedResources) != LiveResources)
        {
            return;
        }
        var failures = new List<Exception>();
        RemoteModelNativeResourceDisposal.Capture(() =>
        {
            var owned = admission;
            owned.Dispose();
        }, failures);
        RemoteModelNativeResourceDisposal.Capture(() =>
        {
            var owned = replay;
            owned.Dispose();
        }, failures);
        RemoteModelNativeResourceDisposal.Capture(() =>
        {
            var owned = stopping;
            owned.Dispose();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
