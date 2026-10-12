using KeyLoad.Orleans;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

using Orleans.Runtime.Services;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelNativeClient : GrainServiceClient<IRemoteModelGrainService>, IAsyncDisposable
{
    private const int SelectedVoter = 0;
    private const long EmptyReplyBytes = 0;
    private readonly CancellationTokenSource stopping;
    private int resourcesDisposed;
    private readonly PartitionHost partition;
    private readonly NodeOptions options;
    private readonly GrainRoutingOptions routing;
    private readonly PartitionMovementGrainServiceClient movement;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly TimeProvider clock;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly IRemoteModelNativeFaultObserver? faultObserver;
    private readonly IRemoteModelNativeReplayObserver? replayObserver;
    private readonly INativeControlledBlobBorrowObserver? blobObserver;

    public RemoteModelNativeClient(IServiceProvider services, PartitionHost partition,
        IOptions<NodeOptions> options, IOptions<OrleansMembershipOptions> membership,
        IOptions<GrainRoutingOptions> routing, PartitionMovementGrainServiceClient movement,
        ReplicaSiloDiscoveryState local, TimeProvider clock) : base(services)
    {
        this.partition = partition; this.options = options.Value; this.routing = routing.Value;
        this.movement = movement; this.local = local; this.clock = clock;
        faultObserver = this.options.RequestCqrsProbe.Enabled
            ? services.GetService(typeof(IRemoteModelNativeFaultObserver)) as IRemoteModelNativeFaultObserver : null;
        replayObserver = this.options.RequestCqrsProbe.Enabled
            ? services.GetService(typeof(IRemoteModelNativeReplayObserver)) as IRemoteModelNativeReplayObserver : null;
        blobObserver = this.options.RequestCqrsProbe.Enabled
            ? services.GetService(typeof(INativeControlledBlobBorrowObserver)) as INativeControlledBlobBorrowObserver : null;
        membership.Value.Validate();
        stopping = new();
        lifetime = new(stopping, DisposeOwnedResources);
    }

    internal async Task<RemoteModelAuthenticatedReply> SendAsync(RegisteredPhysicalOwnerV1 destination,
        PhysicalShardRecord source, RemoteModelNativeChannel channel, ReadOnlyMemory<byte> payload,
        string signature, DateTimeOffset expiry, long maximumReplyBytes, CancellationToken caller)
    {
        using var operation = lifetime.TryEnter()
            ?? throw new ObjectDisposedException(nameof(RemoteModelNativeClient));
        if (maximumReplyBytes is <= EmptyReplyBytes or > RemoteDocumentProtocol.MaximumBodyBytes || expiry <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteModelNativeProtocol.Unavailable); }
        if (payload.IsEmpty || payload.Length > RemoteDocumentProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteModelNativeProtocol.InvalidProof); }
        var remaining = expiry - clock.GetUtcNow();
        var bound = remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime;
        using var deadline = new CancellationTokenSource(bound, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken, deadline.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        _ = RemoteModelNativeIdentity.Secret(options, partition, source);
        _ = RemoteModelNativeIdentity.Secret(options, partition, destination.Owner);
        var address = await movement.ResolveAsync(destination, SelectedVoter, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var current = local.Read();
        if (!current.TransportReady || !source.VoterIds.Contains(current.VoterId, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable); }
        await movement.RequireCallerAsync(current.VoterId, current.SiloAddress, token).ConfigureAwait(false);
        var request = new RemoteModelNativeRequest(channel, payload.ToArray(), signature, source, destination.Owner,
            current.VoterId, current.SiloAddress, destination.Owner.VoterIds[SelectedVoter], address.ToParsableString(),
            Guid.NewGuid(), expiry, string.Empty);
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, source));
        request = request with { SenderSignature = sourceMac.SignNativeRequest(request) };
        var effects = RemoteModelNativeEffects.MayHaveEffects(request.Channel, request.Payload);
        var (received, priorReplayEffects) = await DispatchObservedAsync(destination, address, request, sourceMac, maximumReplyBytes, token).ConfigureAwait(false);
        RemoteModelNativeReply reply;
        try
        {
            if (faultObserver is not null)
            { await faultObserver.AfterDispatchAsync(request, received, token).ConfigureAwait(false); }
            reply = await VerifyReplyAsync(destination, request, received, maximumReplyBytes, token).ConfigureAwait(false);
        }
        catch (Exception error) when ((effects || priorReplayEffects) && NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error); }
        // A correctly authenticated receiver refusal is interpreted only after exact dispatch identity checks.
        if (reply.Error is { } failure)
        {
            var originalFailure = Errors.Fail(failure, RemoteModelNativeProtocol.Unavailable);
            if (priorReplayEffects)
            { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, originalFailure); }
            throw originalFailure;
        }
        return new(reply.Payload, reply.Signature, request.DestinationSiloAddress) { MayHaveEffects = effects };
    }

    private async Task<(RemoteModelNativeReply Reply, bool PriorReplayEffects)> DispatchObservedAsync(RegisteredPhysicalOwnerV1 destination,
        SiloAddress address, RemoteModelNativeRequest request, RemoteDocumentMac sourceMac,
        long maximumReplyBytes, CancellationToken token)
    {
        var replayEffects = false;
        try
        {
            await NativeControlledBlobBorrowObservation.ObserveAsync(blobObserver, this, destination, address,
                request, sourceMac, maximumReplyBytes, token).ConfigureAwait(false);
            if (replayObserver?.Select(request, token) is { } mode)
            {
                var replay = new RemoteModelNativeReplayExchange(this, destination, request, maximumReplyBytes, mode, token);
                try
                {
                    await using var ownedReplay = replay;
                    await replayObserver.ObserveAsync(request, replay, token).ConfigureAwait(false);
                    await RequireBeforeDispatchAsync(destination, request, sourceMac, token).ConfigureAwait(false);
                }
                finally { replayEffects |= replay.InvokedEffects; }
            }
            if (faultObserver is not null)
            {
                await faultObserver.BeforeDispatchAsync(request, token).ConfigureAwait(false);
                await RequireBeforeDispatchAsync(destination, request, sourceMac, token).ConfigureAwait(false);
            }
            return (await DispatchAsync(address, request, token).ConfigureAwait(false), replayEffects);
        }
        catch (Exception error) when (replayEffects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error); }
    }

    internal Task<RemoteModelNativeReply> VerifyReplyAsync(RegisteredPhysicalOwnerV1 destination,
        RemoteModelNativeRequest request, RemoteModelNativeReply received, long maximum, CancellationToken token)
        => RemoteModelNativeReplyVerification.VerifyAsync(this, destination, request, received, maximum,
            options, partition, movement, local, clock, token);

    internal async Task<RemoteModelNativeReply> DispatchAsync(SiloAddress address,
        RemoteModelNativeRequest request, CancellationToken token)
    {
        var effects = RemoteModelNativeEffects.MayHaveEffects(request.Channel, request.Payload);
        try
        { return await GetGrainService(address).ExchangeAsync(request, token).ConfigureAwait(false); }
        catch (Exception error) when (effects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error); }
        catch (Exception error) when (error is OrleansException or TimeoutException or ObjectDisposedException)
        { throw Errors.Fail(effects ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable, error); }
    }

    internal async Task RequireBeforeDispatchAsync(RegisteredPhysicalOwnerV1 destination,
        RemoteModelNativeRequest request, RemoteDocumentMac sourceMac, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireCurrentAsync(destination, request.DestinationSiloAddress, token).ConfigureAwait(false);
        await movement.RequireCallerAsync(request.CallerVoter, request.CallerSiloAddress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!sourceMac.VerifyNativeRequest(request))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        if (request.ExpiresAt <= clock.GetUtcNow() || local.Read().SiloAddress != request.CallerSiloAddress)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteModelNativeProtocol.Unavailable); }
    }

    internal async Task<RemoteModelNativeReplayResult> ReplayCapturedAsync(RegisteredPhysicalOwnerV1 destination,
        RemoteModelNativeRequest original, long maximumReplyBytes, RemoteModelNativeReplayVariation variation,
        RemoteModelNativeReplayExchange owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.DestinationOwner != destination.Owner || original.ExpiresAt <= clock.GetUtcNow()
            || maximumReplyBytes is <= EmptyReplyBytes or > RemoteDocumentProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, original.SourceOwner));
        if (!sourceMac.VerifyNativeRequest(original) || !sourceMac.Verify(original.Payload.Span, original.Signature, false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        var payload = RemoteModelNativeReplayPayload.Create(original, variation);
        _ = RemoteModelNativeIdentity.Secret(options, partition, destination.Owner);
        var address = await movement.ResolveAsync(destination, SelectedVoter, token).ConfigureAwait(false);
        var current = local.Read();
        token.ThrowIfCancellationRequested();
        if (!current.TransportReady || !original.SourceOwner.VoterIds.Contains(current.VoterId, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable); }
        await movement.RequireCallerAsync(current.VoterId, current.SiloAddress, token).ConfigureAwait(false);
        var request = original with { Payload = payload.ToArray(), CallerVoter = current.VoterId,
            CallerSiloAddress = current.SiloAddress, DestinationVoter = destination.Owner.VoterIds[SelectedVoter],
            DestinationSiloAddress = address.ToParsableString(), Nonce = Guid.NewGuid(), SenderSignature = string.Empty };
        request = request with { SenderSignature = sourceMac.SignNativeRequest(request) };
        token.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteModelNativeProtocol.Unavailable); }
        owner.MarkInvoked(variation);
        var received = await DispatchAsync(address, request, token).ConfigureAwait(false);
        RemoteModelNativeReply reply;
        try
        { reply = await VerifyReplyAsync(destination, request, received, maximumReplyBytes, token).ConfigureAwait(false); }
        catch (Exception error) when (RemoteModelNativeEffects.MayHaveEffects(request.Channel, request.Payload)
            && NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error); }
        // Preserve signed native terminal refusal as actual evidence; it is not an acknowledgement.
        return new(request, reply);
    }

    internal async Task RequireCurrentAsync(RegisteredPhysicalOwnerV1 destination, string address, CancellationToken token)
    {
        var actual = await movement.ResolveAsync(destination, SelectedVoter, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!string.Equals(actual.ToParsableString(), address, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable); }
    }
    internal Task StopAsync() => lifetime.StopAsync();
    public async ValueTask DisposeAsync()
    {
        try
        { await lifetime.DisposeAsync().ConfigureAwait(false); }
        finally
        { DisposeOwnedResources(); }
    }

    private void DisposeOwnedResources()
    {
        const int DisposedResources = 1;
        const int LiveResources = 0;
        if (Interlocked.Exchange(ref resourcesDisposed, DisposedResources) == LiveResources)
        { stopping.Dispose(); }
    }
}
