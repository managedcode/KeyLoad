using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Bounds every typed field before native serialization and authenticates a fresh diagnostic purpose.</summary>
internal sealed class RequestCqrsProbeNativeAuthentication
{
    private const int EmptySignatureLength = 0;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly RequestCqrsProbeNativeTrust trust;
    private readonly ReplicaConfiguration configuration;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly IOptions<RequestProbeExecutionOptions> probe;
    private readonly TimeProvider clock;

    internal RequestCqrsProbeNativeAuthentication(RequestCqrsProbeNativeTrust trust,
        IOptions<ReplicaConfiguration> configuration, IOptions<OrleansMembershipOptions> membership,
        IOptions<RequestProbeExecutionOptions> probe, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Value);
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(membership.Value);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(probe.Value);
        ArgumentNullException.ThrowIfNull(clock);
        this.trust = trust;
        this.configuration = configuration.Value;
        this.membership = membership;
        this.probe = probe;
        this.clock = clock;
    }

    internal RequestCqrsProbeNativeRequest Create(RequestCqrsProbeNativeIdentity identity, PhysicalShardRecord owner,
        string voter, string address, RequestCqrsProbeNativeClaim? claim)
    {
        var method = claim is null ? RequestCqrsProbeNativeMethod.Locate : RequestCqrsProbeNativeMethod.Notify;
        var request = new RequestCqrsProbeNativeRequest(RequestCqrsProbeNativeProtocol.Version, method, identity,
            trust.Local, trust.Voter, trust.Address, owner, voter, address, Guid.NewGuid(),
            clock.GetUtcNow().Add(configuration.RpcTimeout), claim, string.Empty);
        RequireRequestShape(request, signed: false);
        Bound(request, membership.Value.MaximumRequestBytes);
        return request with
        {
            Signature = trust.Mac(trust.Local).SignProbeRequest(request)
        };
    }

    internal RequestCqrsProbeNativeRequest Verify(RequestCqrsProbeNativeRequest request, RequestCqrsProbeNativeMethod method)
    {
        RequireRequestShape(request, signed: true);
        if (request.Method != method || !RequestCqrsProbeNativeTrust.Same(trust.Local, request.DestinationOwner)
            || request.DestinationVoter != trust.Voter || request.DestinationSiloAddress != trust.Address)
        {
            throw Invalid();
        }
        var ownerMac = trust.Mac(request.CallerOwner);
        var now = clock.GetUtcNow();
        if (request.ExpiresAt.Offset != TimeSpan.Zero || request.ExpiresAt <= now
            || request.ExpiresAt > now.Add(configuration.RpcTimeout).Add(membership.Value.ClockSkew))
        {
            throw Invalid();
        }
        var bytes = Bound(request, membership.Value.MaximumRequestBytes);
        var owned = NativeSerialization.Deserialize<RequestCqrsProbeNativeRequest>(bytes);
        if (!ownerMac.VerifyProbeRequest(owned))
        {
            throw Invalid();
        }
        RequireCurrent(owned);
        return owned;
    }

    internal RequestCqrsProbeNativeReply Reply(RequestCqrsProbeNativeRequest request, RequestCqrsProbeNativeClaim? claim,
        bool acknowledged, ErrorCode? error = null)
    {
        var reply = new RequestCqrsProbeNativeReply(RequestCqrsProbeNativeProtocol.Version, request.Method,
            request.Nonce, claim, acknowledged, error, string.Empty);
        RequireReplyShape(reply, signed: false);
        Bound(reply, membership.Value.MaximumReplyBytes);
        return reply with
        {
            Signature = trust.Mac(trust.Local).SignProbeReply(reply)
        };
    }

    internal RequestCqrsProbeNativeReply VerifyReply(RequestCqrsProbeNativeRequest request, RequestCqrsProbeNativeReply reply)
    {
        RequireReplyShape(reply, signed: true);
        if (reply.Nonce != request.Nonce || reply.Method != request.Method)
        {
            throw Invalid();
        }
        var bytes = Bound(reply, membership.Value.MaximumReplyBytes);
        var owned = NativeSerialization.Deserialize<RequestCqrsProbeNativeReply>(bytes);
        if (!trust.Mac(request.DestinationOwner).VerifyProbeReply(owned))
        {
            throw Invalid();
        }
        if (request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Cancelled, RequestCqrsProbeNativeProtocol.Unavailable);
        }
        RequireCurrent(request);
        if (owned.Error is { } code)
        {
            throw Errors.Fail(code, RequestCqrsProbeNativeProtocol.Unavailable);
        }
        if (owned.Claim is { } claim && (claim.Identity != request.Identity
            || !RequestCqrsProbeNativeTrust.Same(request.DestinationOwner, claim.Owner)
            || claim.Voter != request.DestinationVoter || claim.SiloAddress != request.DestinationSiloAddress))
        {
            throw Invalid();
        }
        if (request.Method == RequestCqrsProbeNativeMethod.Notify
            && (!owned.Acknowledged || owned.Claim != request.Claim && !SameClaim(owned.Claim, request.Claim)))
        {
            throw Invalid();
        }
        return owned;
    }

    internal void RequireCurrent(RequestCqrsProbeNativeRequest request)
    {
        _ = trust.RequireCurrent(request.CallerOwner, request.CallerVoter, request.CallerSiloAddress);
        _ = trust.RequireCurrent(request.DestinationOwner, request.DestinationVoter, request.DestinationSiloAddress);
    }

    internal static bool SameClaim(RequestCqrsProbeNativeClaim? left, RequestCqrsProbeNativeClaim? right)
        => left is not null && right is not null && left.SessionId == right.SessionId && left.ArmId == right.ArmId
            && left.Identity == right.Identity && left.ArmSha256 == right.ArmSha256 && left.Voter == right.Voter
            && left.SiloAddress == right.SiloAddress && RequestCqrsProbeNativeTrust.Same(left.Owner, right.Owner);

    private void RequireRequestShape(RequestCqrsProbeNativeRequest? request, bool signed)
    {
        if (request is null || request.Version != RequestCqrsProbeNativeProtocol.Version || !Enum.IsDefined(request.Method)
            || !Identity(request.Identity) || !Owner(request.CallerOwner) || !Owner(request.DestinationOwner)
            || !Text(request.CallerVoter) || !Text(request.CallerSiloAddress)
            || !Text(request.DestinationVoter) || !Text(request.DestinationSiloAddress) || request.Nonce == Guid.Empty
            || signed && !Signature(request.Signature) || !signed && request.Signature is not { Length: EmptySignatureLength }
            || request.Method == RequestCqrsProbeNativeMethod.Locate && request.Claim is not null
            || request.Method == RequestCqrsProbeNativeMethod.Notify && !Claim(request.Claim))
        {
            throw Invalid();
        }
        if (request.Claim is { } claim && (claim.Identity != request.Identity
            || !RequestCqrsProbeNativeTrust.Same(request.DestinationOwner, claim.Owner)
            || claim.Voter != request.DestinationVoter || claim.SiloAddress != request.DestinationSiloAddress))
        {
            throw Invalid();
        }
    }

    private void RequireReplyShape(RequestCqrsProbeNativeReply? reply, bool signed)
    {
        if (reply is null || reply.Version != RequestCqrsProbeNativeProtocol.Version || !Enum.IsDefined(reply.Method)
            || reply.Nonce == Guid.Empty || signed && !Signature(reply.Signature) || !signed && reply.Signature is not { Length: EmptySignatureLength }
            || reply.Error is { } code && (!Enum.IsDefined(code) || reply.Claim is not null || reply.Acknowledged)
            || reply.Claim is not null && !Claim(reply.Claim)
            || reply.Method == RequestCqrsProbeNativeMethod.Locate && reply.Acknowledged
            || reply.Method == RequestCqrsProbeNativeMethod.Notify && reply.Error is null && (!reply.Acknowledged || reply.Claim is null))
        {
            throw Invalid();
        }
    }

    private bool Identity(RequestCqrsProbeNativeIdentity? identity)
        => identity is not null && identity.RequestId != Guid.Empty && identity.ConnectionId != Guid.Empty
            && (identity.PrincipalId is null || Text(identity.PrincipalId, probe.Value.MaximumPrincipalBytes))
            && (identity.ReadKind is null || Enum.IsDefined(identity.ReadKind.Value))
            && (identity.CommandKind is null || Enum.IsDefined(identity.CommandKind.Value))
            && (identity.ReadKind is null) != (identity.CommandKind is null)
            && (identity.ReadKind is not null ? identity.CommandId == Guid.Empty : identity.CommandId != Guid.Empty);
    private bool Owner(PhysicalShardRecord? owner)
        => owner is not null && owner.PhysicalShardId != Guid.Empty && owner.Incarnation != Guid.Empty
            && !owner.VoterIds.IsDefault && owner.VoterIds.Length == RequestCqrsProbeNativeProtocol.FixedMembers
            && owner.VoterIds.All(voter => Text(voter))
            && owner.VoterIds.Distinct(StringComparer.Ordinal).Count() == RequestCqrsProbeNativeProtocol.FixedMembers;
    private bool Claim(RequestCqrsProbeNativeClaim? claim)
        => claim is not null && Text(claim.SessionId) && Guid.TryParseExact(claim.SessionId, RequestCqrsProbeProtocol.SessionIdFormat, out var session)
            && session != Guid.Empty && claim.ArmId != Guid.Empty && Identity(claim.Identity) && Owner(claim.Owner)
            && Text(claim.Voter) && Text(claim.SiloAddress) && Signature(claim.ArmSha256);
    private bool Text(string? value, int? maximum = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try
        {
            return Utf8.GetByteCount(value) <= (maximum ?? membership.Value.MaximumAddressBytes);
        }
        catch (EncoderFallbackException) { return false; }
    }
    private static bool Signature(string? value)
        => value is { Length: ReplicaTransportProtocol.HashHexCharacters } && value.All(char.IsAsciiHexDigitLower);
    private static byte[] Bound<T>(T value, int maximum)
    {
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length > maximum)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RequestCqrsProbeNativeProtocol.Unavailable);
        }
        return bytes;
    }
    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable);
}
