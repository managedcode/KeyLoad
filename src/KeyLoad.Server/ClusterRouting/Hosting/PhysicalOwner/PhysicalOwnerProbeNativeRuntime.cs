using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeNativeRuntime : IAsyncDisposable
{
    private const string NonceFormat = "N";
    private readonly NodeOptions options;
    private readonly OrleansNode node;
    private readonly PartitionHost partition;
    private readonly PhysicalOwnerProbeReceiver receiver;
    private readonly PhysicalOwnerProbeWorkOwner owner;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;
    private readonly ILogger<PhysicalOwnerProbeNativeRuntime> logger;
    private readonly PhysicalOwnerProbeNativeReplay outerReplay;
    private readonly PhysicalOwnerProbeNativeReplay innerReplay;
    private readonly Lock stopping = new();
    private Task? shutdown;

    internal PhysicalOwnerProbeNativeRuntime(IOptions<NodeOptions> options, OrleansNode node, PartitionHost partition,
        PhysicalOwnerProbeReceiver receiver, PhysicalOwnerProbeWorkOwner owner, IOptions<OrleansMembershipOptions> membership,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, ILogger<PhysicalOwnerProbeNativeRuntime> logger)
    {
        this.options = options.Value;
        this.node = node;
        this.partition = partition;
        this.receiver = receiver;
        this.owner = owner;
        this.membership = membership;
        this.routing = routing.Value;
        this.clock = clock;
        this.logger = logger;
        outerReplay = new(clock, membership);
        innerReplay = new(clock, membership);
    }

    internal async Task<PartitionMovementGrainServiceReply> ProbeOwnerAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var trace = new PhysicalOwnerProbeTrace();
        var authenticated = false;
        PhysicalOwnerProbeWorkOwner.Operation? operation = null;
        CancellationTokenSource? deadline = null;
        PartitionMovementMac? mac = null;
        try
        {
            operation = owner.Acquire(request.Nonce);
            deadline = new CancellationTokenSource(routing.ExecutionLifetime, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.ShutdownToken, deadline.Token);
            var token = linked.Token;
            token.ThrowIfCancellationRequested();
            var now = clock.GetUtcNow();
            var local = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
            RequireOuterShape(request, local, now);
            request = request with
            {
                Payload = request.Payload.ToArray()
            };
            mac = CreateControlMac();
            if (!mac.VerifyNativeRequest(request))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
            }
            RequireCurrent(request, token);
            authenticated = true;
            trace.Stage = PhysicalOwnerProbeStage.ReplayAdmission;
            if (!outerReplay.TryUse(request.Nonce.ToString(NonceFormat), request.ExpiresAt))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
            }
            var reply = await ReadOriginalAsync(request, trace, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RequireCurrent(request, token);
            if (request.ExpiresAt <= clock.GetUtcNow() || reply.EndpointDiscovery.SiloAddress != request.DestinationSiloAddress)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
            }
            trace.Stage = PhysicalOwnerProbeStage.ReplyEncoding;
            return SignReply(reply, request, mac);
        }
        catch (KeyLoadException error)
        {
            return Failure(request, mac, authenticated, error.Code, trace, deadline, operation);
        }
        catch (OperationCanceledException)
        {
            return Failure(request, mac, authenticated, ErrorCode.Cancelled, trace, deadline, operation);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return Failure(request, mac, authenticated, ErrorCode.OwnershipLost, trace, deadline, operation);
        }
        finally
        {
            mac?.Dispose();
            deadline?.Dispose();
            operation?.Dispose();
        }
    }

    private PartitionMovementGrainServiceReply SignReply(PhysicalOwnerProbeReplyV1 reply,
        PartitionMovementGrainServiceRequest request, PartitionMovementMac mac)
    {
        var encoded = PhysicalOwnerProbeWire.Encode(reply);
        using var replyMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.PeerSecret);
        var result = new PartitionMovementGrainServiceReply(encoded, replyMac.Sign(encoded, reply: true),
            null, null, request.Nonce, string.Empty);
        return result with
        {
            SenderSignature = mac.SignNativeReply(result)
        };
    }

    private void RequireOuterShape(PartitionMovementGrainServiceRequest request, ReplicaSiloDiscovery local,
        DateTimeOffset now)
    {
        if (!options.PhysicalGroupTrust.RegisterPhysicalOwners || options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Destination
            || request.Channel != PartitionMovementGrainServiceChannel.PhysicalOwnerProbe || !request.Query
            || request.Nonce == Guid.Empty || request.ExpiresAt.Offset != TimeSpan.Zero || request.ExpiresAt <= now || request.ExpiresAt > now + routing.ExecutionLifetime
            || request.Payload.IsEmpty || request.Payload.Length > PhysicalOwnerProbeProtocol.MaximumBodyBytes
            || request.Payload.Length > partition.Database.Limits.MaxBatchBytes
            || request.Signature is not { Length: PhysicalOwnerProbeProtocol.SignatureCharacters }
            || request.SenderSignature is not { Length: PhysicalOwnerProbeProtocol.SignatureCharacters }
            || request.DestinationVoter != partition.Configuration.LocalId
            || request.DestinationSiloAddress != local.SiloAddress
            || !PhysicalOwnerConfiguredTuples.Control(options, partition).Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
    }

    private async Task<PhysicalOwnerProbeReplyV1> ReadOriginalAsync(PartitionMovementGrainServiceRequest request,
        PhysicalOwnerProbeTrace trace, CancellationToken token)
    {
        trace.Stage = PhysicalOwnerProbeStage.Mac;
        using var callerMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
        if (!callerMac.Verify(request.Payload.Span, request.Signature, reply: false))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        trace.Stage = PhysicalOwnerProbeStage.Decoding;
        var call = PhysicalOwnerProbeWire.DecodeCall(request.Payload.Span);
        receiver.Validate(call, token);
        if (call.CallerVoter != request.CallerVoter || call.CallerSiloAddress != request.CallerSiloAddress
            || call.ExpiresAt != request.ExpiresAt || !innerReplay.TryUse(call.Nonce, call.ExpiresAt))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        return await receiver.ReadAsync(call, trace, token).ConfigureAwait(false);
    }

    private PartitionMovementGrainServiceReply Failure(PartitionMovementGrainServiceRequest request,
        PartitionMovementMac? mac, bool authenticated, ErrorCode error, PhysicalOwnerProbeTrace trace,
        CancellationTokenSource? deadline, PhysicalOwnerProbeWorkOwner.Operation? operation)
    {
        PhysicalOwnerRegistrationLog.ReceiverFailed(logger, error, trace, deadline?.IsCancellationRequested == true,
            operation?.ShutdownToken.IsCancellationRequested == true);
        var reply = new PartitionMovementGrainServiceReply(ReadOnlyMemory<byte>.Empty, string.Empty,
            error, PartitionMovementProtocol.Unavailable, request.Nonce, string.Empty);
        return authenticated && mac is not null ? reply with
        {
            SenderSignature = mac.SignNativeReply(reply)
        } : reply;
    }

    private void RequireCurrent(PartitionMovementGrainServiceRequest request, CancellationToken token)
    {
        PhysicalOwnerProbeNativeIdentity.RequireCurrent(node, request.CallerVoter, request.CallerSiloAddress, membership, token);
        PhysicalOwnerProbeNativeIdentity.RequireCurrent(node, request.DestinationVoter, request.DestinationSiloAddress, membership, token);
    }

    private PartitionMovementMac CreateControlMac()
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition.Configuration);
        var secret = PartitionMovementNativeDiscoveryIdentity.Secret(options, partition.Configuration, control);
        var key = Convert.FromBase64String(secret);
        try
        {
            return new(key, partition.Database.Limits.MaxBatchBytes);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal Task StopAsync()
    {
        lock (stopping)
        {
            return shutdown ??= StopCoreAsync();
        }
    }
    private async Task StopCoreAsync()
    {
        await owner.StopAsync().ConfigureAwait(false);
        outerReplay.Dispose();
        innerReplay.Dispose();
    }
    public ValueTask DisposeAsync() => new(StopAsync());
}
