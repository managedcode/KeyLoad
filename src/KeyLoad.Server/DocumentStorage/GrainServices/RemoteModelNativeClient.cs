using KeyLoad.Clustering;
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
    private readonly SiloStorageIdentityProvider local;
    private readonly TimeProvider clock;
    private readonly ReplicaDiscoveryLifetime lifetime;

    public RemoteModelNativeClient(IServiceProvider services, PartitionHost partition,
        IOptions<NodeOptions> options, IOptions<OrleansMembershipOptions> membership,
        IOptions<GrainRoutingOptions> routing, PartitionMovementGrainServiceClient movement,
        SiloStorageIdentityProvider local, TimeProvider clock) : base(services)
    {
        this.partition = partition;
        this.options = options.Value;
        this.routing = routing.Value;
        this.movement = movement;
        this.local = local;
        this.clock = clock;
        membership.Value.Validate();
        stopping = new();
        lifetime = new(stopping, DisposeOwnedResources);
    }

    internal Task<RemoteModelAuthenticatedReply> SendAsync(RegisteredPhysicalOwnerV1 destination,
        PhysicalShardRecord source, RemoteModelNativeChannel channel, ReadOnlyMemory<byte> payload,
        string signature, DateTimeOffset expiry, long maximumReplyBytes, CancellationToken caller)
        => SendCoreAsync(destination, source, SelectedVoter, channel, payload, signature, expiry,
            maximumReplyBytes, caller);

    internal Task<RemoteModelAuthenticatedReply> SendAnnProjectionLifetimeAsync(RegisteredPhysicalOwnerV1 destination,
        PhysicalShardRecord source, int voterIndex, ReadOnlyMemory<byte> payload,
        string signature, DateTimeOffset expiry, long maximumReplyBytes, CancellationToken caller)
        => SendCoreAsync(destination, source, voterIndex, RemoteModelNativeChannel.ResourceLifetimeAnnRead,
            payload, signature, expiry, maximumReplyBytes, caller);

    private async Task<RemoteModelAuthenticatedReply> SendCoreAsync(RegisteredPhysicalOwnerV1 destination,
        PhysicalShardRecord source, int voterIndex, RemoteModelNativeChannel channel, ReadOnlyMemory<byte> payload,
        string signature, DateTimeOffset expiry, long maximumReplyBytes, CancellationToken caller)
    {
        using var operation = lifetime.TryEnter()
            ?? throw new ObjectDisposedException(nameof(RemoteModelNativeClient));
        if (maximumReplyBytes is <= EmptyReplyBytes or > RemoteDocumentProtocol.MaximumBodyBytes || expiry <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteModelNativeProtocol.Unavailable);
        }
        if (payload.IsEmpty || payload.Length > RemoteDocumentProtocol.MaximumBodyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteModelNativeProtocol.InvalidProof);
        }
        var remaining = expiry - clock.GetUtcNow();
        var bound = remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime;
        using var deadline = new CancellationTokenSource(bound, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken, deadline.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        _ = RemoteModelNativeIdentity.Secret(options, partition, source);
        _ = RemoteModelNativeIdentity.Secret(options, partition, destination.Owner);
        var address = await movement.ResolveAsync(destination, voterIndex, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var current = local.Read();
        if (!current.TransportReady || !source.VoterIds.Contains(current.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable);
        }
        await movement.RequireCallerAsync(current.VoterId, current.SiloAddress, token).ConfigureAwait(false);
        var request = new RemoteModelNativeRequest(channel, payload.ToArray(), signature, source, destination.Owner,
            current.VoterId, current.SiloAddress, destination.Owner.VoterIds[voterIndex], address.ToParsableString(),
            Guid.NewGuid(), expiry, string.Empty);
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, source));
        request = request with
        {
            SenderSignature = sourceMac.SignNativeRequest(request)
        };
        var effects = RemoteModelNativeEffects.MayHaveEffects(request.Channel, request.Payload);
        var received = await DispatchAsync(address, request, token).ConfigureAwait(false);
        RemoteModelNativeReply reply;
        try
        {
            reply = await VerifyReplyAsync(destination, request, received, maximumReplyBytes, token).ConfigureAwait(false);
        }
        catch (Exception error) when (effects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error);
        }
        return RemoteModelNativeObservedExchange.Complete(reply, request, effects);
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
        {
            return await GetGrainService(address).ExchangeAsync(request, token).ConfigureAwait(false);
        }
        catch (Exception error) when (effects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error);
        }
        catch (Exception error) when (error is OrleansException or TimeoutException or ObjectDisposedException)
        {
            throw Errors.Fail(effects ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable, error);
        }
    }

    internal Task RequireCurrentAsync(RegisteredPhysicalOwnerV1 destination, string address, CancellationToken token)
        => RequireCurrentAsync(destination, destination.Owner.VoterIds[SelectedVoter], address, token);

    internal async Task RequireCurrentAsync(RegisteredPhysicalOwnerV1 destination, string voter,
        string address, CancellationToken token)
    {
        var index = destination.Owner.VoterIds.IndexOf(voter);
        var actual = await movement.ResolveAsync(destination, index, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!string.Equals(actual.ToParsableString(), address, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable);
        }
    }
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
        if (Interlocked.Exchange(ref resourcesDisposed, DisposedResources) == LiveResources)
        {
            stopping.Dispose();
        }
    }
}
