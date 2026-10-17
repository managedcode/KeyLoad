using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphNativeRuntime : IAsyncDisposable
{
    private const int NoWaitMilliseconds = 0;
    private const int Unjoined = 0;
    private const int Joined = 1;
    private const string Attached = "The original remote graph runtime is already attached.";
    private const string NonceFormat = "N";
    private readonly PartitionHost partition;
    private readonly NodeOptions options;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;
    private readonly GrainRoutingOptions routing;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly TimeProvider clock;
    private readonly ILogger<RemoteGraphNativeRuntime> logger;
    private readonly Func<NativeRequestWorkOwner> requestWork;
    private readonly RemoteGraphNativeResources resources;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly RemoteGraphNativeAdmission identity;
    private RemoteGraphRuntime? runtime;
    private int joined = Unjoined;

    public RemoteGraphNativeRuntime(PartitionHost partition, IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membershipOptions, IOptions<GrainRoutingOptions> routing,
        ISiloStatusOracle oracle, ReplicaSiloDiscoveryState local, TimeProvider clock,
        ILogger<RemoteGraphNativeRuntime> logger, Func<NativeRequestWorkOwner> requestWork)
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
        identity = new(partition, options, membershipOptions, oracle, local, clock);
        resources = new(clock, membershipOptions);
        try
        {
            lifetime = new(resources.Stopping, resources.Dispose);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseFailedCreation(error);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseFailedCreation(error);
            throw;
        }
    }

    internal bool IsJoined => Volatile.Read(ref joined) == Joined;

    internal void Attach(RemoteGraphRuntime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Interlocked.CompareExchange(ref runtime, owner, null) is not null)
        {
            throw new InvalidOperationException(Attached);
        }
    }

    internal async Task<RemoteGraphNativeReplyV1> ExchangeAsync(RemoteGraphNativeRequestV1 original,
        CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(original);
        using var operation = lifetime.TryEnter() ?? throw new ObjectDisposedException(nameof(RemoteGraphNativeRuntime));
        caller.ThrowIfCancellationRequested();
        operation.ShutdownToken.ThrowIfCancellationRequested();
        if (!await resources.Admission.WaitAsync(NoWaitMilliseconds, caller).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphNativeProtocol.Unavailable);
        }
        try
        {
            RemoteGraphNativeIdentity.RequireShape(original, partition.Database.Limits.MaxBatchBytes, membershipOptions);
            identity.RequireCurrent(original);
            var remaining = original.ExpiresAt - clock.GetUtcNow();
            if (remaining > routing.RequestLifetime)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
            }
            var bound = remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime;
            using var deadline = new CancellationTokenSource(bound, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken, deadline.Token);
            var request = original with
            {
                Payload = original.Payload.ToArray()
            };
            using var sourceMac = RemoteGraphNativeMac.Create(options, partition, request.SourceOwner);
            if (!sourceMac.VerifyRemoteGraphRequest(request))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
            }
            using var targetMac = RemoteGraphNativeMac.Create(options, partition, request.DestinationOwner);
            return await RemoteGraphNativeWorkExecution.ExecuteAsync(requestWork(), partition.Database, request,
                (binding, token) => ExecuteBoundAsync(request, targetMac, binding, token), linked.Token).ConfigureAwait(false);
        }
        finally
        {
            resources.Admission.Release();
        }
    }

    private Task<RemoteGraphNativeReplyV1> ExecuteBoundAsync(RemoteGraphNativeRequestV1 request,
        PartitionMovementMac targetMac, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var work = partition.Database.CreateRemoteGraphReadWork(request.ExpiresAt, token);
        work.AttachResourceLifetimeBinding(binding);
        work.ChargeBytes(request.Payload.Length);
        return ExecuteAdmittedAsync(request, targetMac, work, binding, token);
    }

    private async Task<RemoteGraphNativeReplyV1> ExecuteAdmittedAsync(RemoteGraphNativeRequestV1 request,
        PartitionMovementMac targetMac, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var boundary = new RemoteGraphEffectBoundary();
        RemoteGraphNativeReplyV1 reply;
        try
        {
            identity.RequireCurrent(request);
            token.ThrowIfCancellationRequested();
            if (!resources.Replay.TryUse(request.Nonce.ToString(NonceFormat)))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
            }
            var owner = Volatile.Read(ref runtime)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
            var payload = await owner.ExecuteNativeAsync(request, boundary, work, binding, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            identity.RequireCurrent(request);
            work.Check();
            if (payload.IsEmpty || payload.Length > partition.Database.Limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
            }
            work.ChargeBytes(payload.Length);
            reply = new(RemoteGraphNativeProtocol.Version, request.RequestId, request.Nonce,
                payload.ToArray(), null, null, local.Read(), string.Empty);
        }
        catch (KeyLoadException error)
        {
            reply = Failure(request, boundary.IsStarted ? ErrorCode.UnknownWriteOutcome : error.Code);
        }
        catch (OperationCanceledException)
        {
            reply = Failure(request, boundary.IsStarted ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var code = boundary.IsStarted ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
            GrainFailureDiagnostics.Log(logger, error, request.RequestId, GrainFailureStage.CapabilityExecution, code);
            reply = Failure(request, code);
        }
        return reply with
        {
            Signature = targetMac.SignRemoteGraphReply(reply)
        };
    }

    private RemoteGraphNativeReplyV1 Failure(RemoteGraphNativeRequestV1 request, ErrorCode error)
        => new(RemoteGraphNativeProtocol.Version, request.RequestId, request.Nonce,
            ReadOnlyMemory<byte>.Empty, error, RemoteGraphNativeProtocol.Unavailable, local.Read(), string.Empty);

    internal async Task StopAsync()
    {
        await lifetime.DisposeAsync().ConfigureAwait(false);
        Volatile.Write(ref joined, Joined);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        resources.Dispose();
    }

    private void ReleaseFailedCreation(Exception primary)
    {
        var failures = new List<Exception> { primary };
        ServerFailureObserver.Observe(resources.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
