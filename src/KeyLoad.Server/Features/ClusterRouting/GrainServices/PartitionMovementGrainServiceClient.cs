using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;
using Orleans.Runtime.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Addresses the actual native silo and authenticates one bounded exact-byte exchange.</summary>
internal sealed partial class PartitionMovementGrainServiceClient : GrainServiceClient<IPartitionMovementGrainService>
{
    private const int EmptyErrorSignatureLength = 0;
    private readonly PartitionHost partition;
    private readonly NodeOptions options;
    private readonly GrainRoutingOptions routing;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly TimeProvider clock;
    private readonly PartitionMovementNativeDiscovery discovery;

    internal PartitionMovementGrainServiceClient(IServiceProvider services, PartitionHost partition,
        IOptions<NodeOptions> options, IOptions<OrleansMembershipOptions> membership,
        IOptions<GrainRoutingOptions> routing, ISiloStatusOracle oracle,
        ReplicaSiloDiscoveryState local, TimeProvider clock) : base(services)
    {
        this.partition = partition;
        this.options = options.Value;
        this.routing = routing.Value;
        this.local = local;
        this.clock = clock;
        discovery = new(partition, options, membership, oracle, DiscoverAsync);
    }

    internal Task<SiloAddress> ResolveAsync(RegisteredPhysicalOwnerV1 owner, int index, CancellationToken token)
        => discovery.ResolveAsync(owner, index, token);

    internal Task RequireCallerAsync(string voter, string address, CancellationToken token)
        => discovery.RequireCallerAsync(voter, address, token);

    internal Task RequireCallerAsync(string voter, string address,
        Action<PartitionMovementNativeObservationStage>? observeStage, CancellationToken token)
        => discovery.RequireCallerAsync(voter, address, token, observeStage);

    internal Task<PartitionMovementGrainServiceDiscovery> DiscoverAsync(SiloAddress address, Guid nonce, CancellationToken token)
        => GetGrainService(address).DiscoverAsync(nonce, token);

    internal PartitionMovementGrainServiceRequest CreateRequest(PartitionMovementGrainServiceChannel channel,
        bool query, ReadOnlyMemory<byte> payload, string signature, string destination, SiloAddress address)
    {
        if (payload.IsEmpty || payload.Length > partition.Database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.InvalidProof);
        }
        _ = discovery.RequireDestination(destination);
        var current = local.Read();
        if (!current.TransportReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        }
        var request = new PartitionMovementGrainServiceRequest(channel, query, payload.ToArray(), signature,
            current.VoterId, current.SiloAddress, destination, address.ToParsableString(), Guid.NewGuid(),
            clock.GetUtcNow() + routing.RequestLifetime, string.Empty);
        using var mac = CreateMac(ControlSecret());
        return request with
        {
            SenderSignature = mac.SignNativeRequest(request)
        };
    }

    internal async Task<PartitionMovementGrainServiceReply> ExchangeAsync(SiloAddress address,
        PartitionMovementGrainServiceRequest request, CancellationToken token,
        Action<PartitionMovementGrainServiceRequest, PartitionMovementGrainServiceReply>? observed = null)
    {
        token.ThrowIfCancellationRequested();
        var reply = await GetGrainService(address).ExchangeAsync(request, token).ConfigureAwait(false) ?? throw InvalidReply(ReplyNullStage);
        if (!HasBoundedShape(reply))
        {
            throw InvalidReply(ReplyBodyStage);
        }
        if (reply.Payload.Length > partition.Database.Limits.MaxBatchBytes)
        {
            throw InvalidReply(ReplyPayloadStage);
        }
        if (string.IsNullOrEmpty(reply.SenderSignature) || reply.SenderSignature.Length != ReplicaTransportProtocol.HashHexCharacters)
        {
            throw InvalidReply(ReplyOuterSignatureLengthStage);
        }
        reply = reply with
        {
            Payload = reply.Payload.ToArray()
        };
        using var mac = CreateMac(ControlSecret());
        if (reply.Nonce != request.Nonce)
        {
            throw InvalidReply(ReplyNonceStage);
        }
        if (!mac.VerifyNativeReply(reply))
        {
            throw InvalidReply(ReplySignatureStage);
        }
        if (reply.Error is { } error)
        {
            if (!Enum.IsDefined(error) || !reply.Payload.IsEmpty || reply.Signature is not { Length: EmptyErrorSignatureLength }
                || reply.SafeDetail != PartitionMovementProtocol.Unavailable)
            {
                throw InvalidReply(ReplyErrorStage);
            }
            observed?.Invoke(request, reply);
            throw Errors.Fail(error, PartitionMovementProtocol.Unavailable);
        }
        if (reply.Payload.IsEmpty || reply.SafeDetail is not null || string.IsNullOrEmpty(reply.Signature))
        {
            throw InvalidReply(ReplySuccessStage);
        }
        observed?.Invoke(request, reply);
        return reply;
    }

    private const string ReplyNullStage = "null";
    private const string ReplyBodyStage = "body";
    private const string ReplyPayloadStage = "payload";
    private const string ReplyOuterSignatureLengthStage = "outer-signature-length";
    private const string ReplyNonceStage = "nonce";
    private const string ReplySignatureStage = "signature";
    private const string ReplyErrorStage = "error";
    private const string ReplySuccessStage = "success";
    internal const string InvalidReplyStage = "KeyLoad.PartitionMovement.InvalidReplyStage";

    private static KeyLoadException InvalidReply(string stage)
    {
        var failure = Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        failure.Data[InvalidReplyStage] = stage;
        return failure;
    }

    private static bool HasBoundedShape(PartitionMovementGrainServiceReply reply)
        => reply.Error is null
            ? !reply.Payload.IsEmpty && reply.SafeDetail is null
                && reply.Signature is { Length: ReplicaTransportProtocol.HashHexCharacters }
            : reply.Payload.IsEmpty && reply.Signature is { Length: EmptyErrorSignatureLength }
                && reply.SafeDetail == PartitionMovementProtocol.Unavailable;

    private string ControlSecret()
        => PartitionMovementNativeDiscoveryIdentity.Secret(options, partition.Configuration,
            PhysicalOwnerConfiguredTuples.Control(options, partition.Configuration));

    private PartitionMovementMac CreateMac(string configured)
    {
        var key = Convert.FromBase64String(configured);
        try
        {
            return new(key, partition.Database.Limits.MaxBatchBytes);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
