using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed partial class ResourceOwnerInventoryNativeClient : IAsyncDisposable
{
    private readonly PartitionHost partition;
    private readonly PartitionMovementGrainServiceClient movement;
    private readonly ResourceOwnerInventoryNativeAuthority authority;
    private readonly ResourceOwnerInventoryNativeTrust trust;
    private readonly ResourceOwnerInventorySignedReads reads;
    private readonly IOptions<DatabaseLimits> limits;
    private readonly IOptions<GrainRoutingOptions> routing;
    private readonly NodeOptions options;
    private readonly SiloStorageIdentityProvider local;
    private readonly TimeProvider clock;
    private readonly NativeRequestWorkOwner work;

    internal ResourceOwnerInventoryNativeClient(PartitionHost partition, PartitionMovementGrainServiceClient movement,
        ResourceOwnerInventoryNativeAuthority authority, ResourceOwnerInventoryNativeTrust trust,
        ResourceOwnerInventorySignedReads reads, ResourceRetirementNativeExchange retirement, IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing,
        IOptions<NodeOptions> options, SiloStorageIdentityProvider local, TimeProvider clock)
    {
        this.partition = partition;
        this.movement = movement;
        this.authority = authority;
        this.trust = trust;
        this.reads = reads;
        this.retirement = retirement;
        this.limits = limits;
        this.routing = routing;
        this.options = options.Value;
        this.local = local;
        this.clock = clock;
        work = new(routing);
    }

    internal Task<ResourceOwnerInventoryReadReplyV1> SendToVoterAsync(RegisteredPhysicalOwnerV1 destination,
        string voter, ResourceOwnerInventoryReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch,
        CancellationToken token)
        => ExecuteAsync(query.Request.ExpiresAt, NativeRequestWorkKind.ReadCapability, ResourceOwnerInventoryNativeProtocol.Unavailable, caller => SendCoreAsync(destination, voter, query, sourcePrincipalId, sourcePolicyEpoch, caller), token);

    internal Task<ResourceOwnerInventoryReadReplyV1> ExecuteLocalAsync(ResourceOwnerInventoryReadRequestV1 query,
        string sourcePrincipalId, long sourcePolicyEpoch, CancellationToken token)
        => ExecuteAsync(query.Request.ExpiresAt, NativeRequestWorkKind.ReadCapability, ResourceOwnerInventoryNativeProtocol.Unavailable, caller => ReadLocalAsync(query, sourcePrincipalId, sourcePolicyEpoch, caller), token);

    private async Task<ResourceOwnerInventoryReadReplyV1> ReadLocalAsync(ResourceOwnerInventoryReadRequestV1 query,
        string sourcePrincipalId, long sourcePolicyEpoch, CancellationToken token)
    {
        var current = local.Read();
        query = query with
        {
            DestinationVoter = current.VoterId,
            DestinationSiloAddress = current.SiloAddress
        };
        return await reads.ReadAsync(query, sourcePrincipalId, sourcePolicyEpoch, false, token).ConfigureAwait(false);
    }

    private async Task<T> ExecuteAsync<T>(DateTimeOffset expiresAt, NativeRequestWorkKind kind, string unavailable,
        Func<CancellationToken, Task<T>> operation, CancellationToken token) where T : class
    {
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? linked = null;
        T? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lease = work.Acquire(Guid.NewGuid(), kind);
            var remaining = expiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                throw Errors.Fail(ErrorCode.CursorExpired, unavailable);
            }
            deadline = new(remaining < routing.Value.ExecutionLifetime ? remaining : routing.Value.ExecutionLifetime, clock);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, work.ShutdownToken, deadline.Token);
            try
            {
                result = await operation(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (kind == NativeRequestWorkKind.ReadCapability && !token.IsCancellationRequested
                && !work.ShutdownToken.IsCancellationRequested && expiresAt <= clock.GetUtcNow())
            {
                throw Errors.Fail(ErrorCode.CursorExpired, unavailable);
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => deadline?.Dispose(), failures);
        ServerFailureObserver.Observe(() => lease?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, unavailable);
    }

    private async Task<ResourceOwnerInventoryReadReplyV1> SendCoreAsync(RegisteredPhysicalOwnerV1 destination,
        string voter, ResourceOwnerInventoryReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch, CancellationToken token)
    {
        if (!PhysicalOwnerEntryValidation.Valid(destination)
            || !destination.Owner.VoterIds.Contains(voter, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        }
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition.Configuration);
        var address = await authority.ResolveAsync(control, destination, voter, token).ConfigureAwait(false);
        query = query with
        {
            DestinationVoter = voter,
            DestinationSiloAddress = address.ToParsableString()
        };
        var payload = NativeSerialization.Serialize(query);
        try
        {
            return await ExchangeAsync(destination, query, sourcePrincipalId, sourcePolicyEpoch,
                Guid.NewGuid(), payload, token).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private async Task<ResourceOwnerInventoryReadReplyV1> ExchangeAsync(RegisteredPhysicalOwnerV1 destination,
        ResourceOwnerInventoryReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch,
        Guid requestId, byte[] payload, CancellationToken token)
    {
        var current = local.Read();
        var request = new ResourceOwnerInventoryNativeRequest(ResourceOwnerInventoryNativeProtocol.Version,
            requestId, Guid.NewGuid(), sourcePrincipalId, sourcePolicyEpoch,
            PhysicalOwnerConfiguredTuples.Control(options, partition.Configuration), destination,
            current.VoterId, current.SiloAddress, query.DestinationVoter, query.DestinationSiloAddress,
            clock.GetUtcNow(), query.Request.ExpiresAt, query.MaximumReadBytes, query.MaximumExaminedRecords,
            query.MaximumResultBytes, payload, string.Empty);
        using var mac = trust.CreateMac();
        request = request with
        {
            Signature = ResourceOwnerInventoryMac.SignRequest(mac, request, limits)
        };
        ResourceOwnerInventoryEnvelopeValidation.RequireRequest(request, limits, routing, clock);
        await authority.RequireBeforeSendAsync(request, token).ConfigureAwait(false);
        var address = await authority.ResolveAsync(request.Control, request.Destination, request.DestinationVoter, token)
            .ConfigureAwait(false);
        var reply = await movement.ReadResourceInventoryPacketAsync(address, request, token).ConfigureAwait(false);
        if (reply is null || !ResourceOwnerInventoryMac.VerifyReply(mac, reply, limits))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        }
        ResourceOwnerInventoryEnvelopeValidation.RequireReply(request, reply, limits, clock);
        await authority.RequireReplyAuthorityAsync(request, token).ConfigureAwait(false);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        var value = NativeSerialization.Deserialize<ResourceOwnerInventoryReadReplyV1>(reply.Payload.Span);
        ResourceOwnerInventoryNativeQuery.RequireReply(query, value, sourcePrincipalId, sourcePolicyEpoch);
        return value;
    }

    internal bool IsJoined => work.IsJoined;
    internal Task CloseAdmissionAsync() => work.DrainAsync();
    public ValueTask DisposeAsync() => work.DisposeAsync();
}
