using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;
using Orleans.Runtime.Services;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphNativeClient : GrainServiceClient<IRemoteGraphGrainService>, IAsyncDisposable
{
    private const int Unjoined = 0;
    private const int Joined = 1;
    private const int StoppingActive = 0;
    private const int StoppingReleased = 1;
    private readonly PartitionHost partition;
    private readonly NodeOptions options;
    private readonly GrainRoutingOptions routing;
    private readonly PartitionMovementGrainServiceClient movement;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly TimeProvider clock;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly CancellationTokenSource stopping;
    private int stoppingDisposed = StoppingActive;
    private int joined = Unjoined;
    internal bool IsJoined => Volatile.Read(ref joined) == Joined;

    internal RemoteGraphNativeClient(IServiceProvider services, PartitionHost partition, IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membership, IOptions<GrainRoutingOptions> routing,
        PartitionMovementGrainServiceClient movement, ReplicaSiloDiscoveryState local, TimeProvider clock) : base(services)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(clock);
        membership.Value.Validate();
        this.partition = partition;
        this.options = options.Value;
        this.routing = routing.Value;
        this.movement = movement;
        this.local = local;
        this.clock = clock;
        stopping = new();
        try
        {
            lifetime = new(stopping, ReleaseStopping);
        }
        catch (Exception primary) when (NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(ReleaseStopping, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catch (Exception primary) when (!NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(ReleaseStopping, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void ReleaseStopping()
    {
        if (Interlocked.Exchange(ref stoppingDisposed, StoppingReleased) == StoppingActive)
        {
            stopping.Dispose();
        }
    }

    internal async Task<RemoteGraphNativeReplyV1> SendAsync(RegisteredPhysicalOwnerV1 receiver,
        RemoteGraphNativeAction action, string actualPrincipalId, ReadOnlyMemory<byte> originalPayload,
        Guid originalRequestId, DateTimeOffset originalExpiry, ReadExecutionBudget work, CancellationToken caller)
    {
        var reply = await ExchangeVerifiedAsync(receiver, action, actualPrincipalId, originalPayload,
            originalRequestId, originalExpiry, work, caller).ConfigureAwait(false);
        if (reply.Error is { } refusal)
        {
            throw Errors.Fail(refusal, RemoteGraphNativeProtocol.Unavailable);
        }
        return reply;
    }

    internal Task<RemoteGraphNativeReplyV1> SendIncomingProofReplyAsync(RegisteredPhysicalOwnerV1 receiver,
        string actualPrincipalId, ReadOnlyMemory<byte> originalPayload, Guid originalRequestId,
        DateTimeOffset originalExpiry, ReadExecutionBudget work, CancellationToken caller)
        => ExchangeVerifiedAsync(receiver, RemoteGraphNativeAction.IncomingSourceProof, actualPrincipalId,
            originalPayload, originalRequestId, originalExpiry, work, caller);

    private async Task<RemoteGraphNativeReplyV1> ExchangeVerifiedAsync(RegisteredPhysicalOwnerV1 receiver,
        RemoteGraphNativeAction action, string actualPrincipalId, ReadOnlyMemory<byte> originalPayload,
        Guid originalRequestId, DateTimeOffset originalExpiry, ReadExecutionBudget work, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var operation = lifetime.TryEnter() ?? throw new ObjectDisposedException(nameof(RemoteGraphNativeClient));
        work.Check();
        if (!Enum.IsDefined(action) || originalRequestId == Guid.Empty || originalPayload.IsEmpty
            || originalPayload.Length > partition.Database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphNativeProtocol.InvalidProof);
        }
        JsonData.Identifier(actualPrincipalId);
        var remaining = originalExpiry - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero || originalExpiry.Offset != TimeSpan.Zero || remaining > routing.RequestLifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphNativeProtocol.Unavailable);
        }
        var bound = remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime;
        var workRemaining = work.RemainingLifetime;
        bound = bound < workRemaining ? bound : workRemaining;
        using var deadline = new CancellationTokenSource(bound, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, work.Cancellation, operation.ShutdownToken, deadline.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        var sender = PhysicalOwnerConfiguredTuples.Local(options, partition);
        RemoteGraphNativeIdentity.RequireOwner(options, partition, sender);
        RemoteGraphNativeIdentity.RequireOwner(options, partition, receiver);
        var address = await movement.ResolveAsync(receiver, RemoteGraphNativeProtocol.SelectedVoter, token).ConfigureAwait(false);
        var current = local.Read();
        if (!current.TransportReady || !sender.Owner.VoterIds.Contains(current.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
        }
        work.ChargeBytes(originalPayload.Length);
        var request = new RemoteGraphNativeRequestV1(RemoteGraphNativeProtocol.Version, originalRequestId, action,
            actualPrincipalId, sender, receiver, current.VoterId, current.SiloAddress,
            receiver.Owner.VoterIds[RemoteGraphNativeProtocol.SelectedVoter], address.ToParsableString(),
            Guid.NewGuid(), originalExpiry, originalPayload.ToArray(), string.Empty);
        using var mac = RemoteGraphNativeMac.Create(options, partition, sender);
        request = request with
        {
            Signature = mac.SignRemoteGraphRequest(request)
        };
        await RequireCurrentAsync(request, token).ConfigureAwait(false);
        work.Check();
        token.ThrowIfCancellationRequested();
        RemoteGraphNativeReplyV1 reply;
        try
        {
            var received = await GetGrainService(address).ExchangeAsync(request, token).ConfigureAwait(false);
            reply = await RemoteGraphNativeReplyVerification.VerifyAsync(this, partition, options, clock,
                request, received, work, token).ConfigureAwait(false);
        }
        catch (Exception error) when (RemoteGraphNativeProtocol.MayHaveEffects(action) && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteGraphNativeProtocol.Unavailable, error);
        }
        catch (Exception error) when (error is OrleansException or TimeoutException or ObjectDisposedException)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable, error);
        }
        return reply;
    }

    internal async Task RequireCurrentAsync(RemoteGraphNativeRequestV1 request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sourceIndex = request.SourceOwner.Owner.VoterIds.IndexOf(request.CallerVoter);
        var targetIndex = request.DestinationOwner.Owner.VoterIds.IndexOf(request.DestinationVoter);
        var source = await movement.ResolveAsync(request.SourceOwner, sourceIndex, token).ConfigureAwait(false);
        var target = await movement.ResolveAsync(request.DestinationOwner, targetIndex, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var current = local.Read();
        if (!current.TransportReady || current.VoterId != request.CallerVoter
            || current.SiloAddress != request.CallerSiloAddress
            || source.ToParsableString() != request.CallerSiloAddress
            || target.ToParsableString() != request.DestinationSiloAddress || request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
        }
    }

    internal Task StopAsync() => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await lifetime.DisposeAsync().ConfigureAwait(false);
        if (Interlocked.Exchange(ref stoppingDisposed, StoppingReleased) == StoppingActive)
        {
            stopping.Dispose();
        }
        Volatile.Write(ref joined, Joined);
    }
}
