using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Checks bounded shape, native MAC and original correlation without defining retirement authority.</summary>
internal static class ResourceRetirementEnvelopeValidation
{
    private const int Empty = 0;
    private const long EmptyBytes = 0;
    private const long MinimumPolicyEpoch = 0;

    internal static void RequireRequest(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
        => RequireRequestCore(mac, request, limits, routing, clock, current: true);

    internal static void RequireHistoricalRequest(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
        => RequireRequestCore(mac, request, limits, routing, clock, current: false);

    private static void RequireRequestCore(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock, bool current)
    {
        var now = clock.GetUtcNow();
        if (request is null || request.Version != ResourceRetirementNativeProtocol.Version
            || request.RequestId == Guid.Empty || request.Nonce == Guid.Empty
            || request.Command && (request.CommandId == Guid.Empty || request.CommandId == request.RequestId)
            || !request.Command && request.CommandId != Guid.Empty
            || string.IsNullOrWhiteSpace(request.PrincipalId) || request.PolicyEpoch < MinimumPolicyEpoch
            || !PhysicalOwnerEntryValidation.Valid(request.Control) || !PhysicalOwnerEntryValidation.Valid(request.Destination)
            || string.IsNullOrWhiteSpace(request.CallerVoter) || string.IsNullOrWhiteSpace(request.CallerSiloAddress)
            || string.IsNullOrWhiteSpace(request.DestinationVoter) || string.IsNullOrWhiteSpace(request.DestinationSiloAddress)
            || request.IssuedAt > now || request.ExpiresAt <= request.IssuedAt
            || request.ExpiresAt - request.IssuedAt > routing.Value.RequestLifetime
            || request.MaximumReadBytes <= EmptyBytes || request.MaximumReadBytes > limits.Value.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= Empty || request.MaximumExaminedRecords > limits.Value.MaxScanRecords
            || request.MaximumResultBytes <= Empty || request.MaximumResultBytes > limits.Value.MaxBatchBytes
            || request.Payload.IsEmpty || request.Payload.Length > limits.Value.MaxBatchBytes
            || !IsSignature(request.Signature))
        {
            throw Invalid();
        }
        RequireEnvelopeSize(request, limits);
        if (!ResourceRetirementMac.VerifyRequest(mac, request, limits))
        {
            throw Invalid();
        }
        if (current && request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.InvalidProof);
        }
    }

    internal static void RequireReply(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        ResourceRetirementNativeReply reply, IOptions<DatabaseLimits> limits, TimeProvider clock)
        => RequireReplyCore(mac, request, reply, limits, clock, current: true);

    internal static void RequireHistoricalReply(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        ResourceRetirementNativeReply reply, IOptions<DatabaseLimits> limits, TimeProvider clock)
        => RequireReplyCore(mac, request, reply, limits, clock, current: false);

    private static void RequireReplyCore(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        ResourceRetirementNativeReply reply, IOptions<DatabaseLimits> limits, TimeProvider clock, bool current)
    {
        var now = clock.GetUtcNow();
        if (request is null || reply is null || request.Version != ResourceRetirementNativeProtocol.Version
            || reply.Version != request.Version || request.RequestId == Guid.Empty || request.Nonce == Guid.Empty
            || request.Command && (request.CommandId == Guid.Empty || request.CommandId == request.RequestId)
            || !request.Command && request.CommandId != Guid.Empty
            || reply.RequestId != request.RequestId || reply.Nonce != request.Nonce
            || request.IssuedAt > now || request.ExpiresAt <= request.IssuedAt
            || !IsSignature(request.Signature) || reply.RequestSignature != request.Signature
            || !IsSignature(reply.Signature)
            || request.MaximumResultBytes <= Empty || request.MaximumResultBytes > limits.Value.MaxBatchBytes)
        {
            throw Invalid();
        }
        RequireEnvelopeSize(reply, limits);
        if (reply.Error is { } error)
        {
            if (!Enum.IsDefined(error) || !reply.Payload.IsEmpty || reply.SafeDetail != ResourceRetirementNativeProtocol.Unavailable)
            {
                throw Invalid();
            }
        }
        else if (reply.SafeDetail is not null || reply.Payload.IsEmpty || reply.Payload.Length > request.MaximumResultBytes)
        {
            throw Invalid();
        }
        if (!ResourceRetirementMac.VerifyReply(mac, reply, limits))
        {
            throw Invalid();
        }
        if (current && request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.InvalidProof);
        }
    }

    internal static void RequireEnvelopeSize<T>(T value, IOptions<DatabaseLimits> limits)
    {
        if (NativeSerialization.Measure(value) > limits.Value.MaxBatchBytes)
        {
            throw Invalid();
        }
    }

    internal static bool IsSignature(string? value)
        => value is { Length: ReplicaTransportProtocol.HashHexCharacters }
            && value.All(char.IsAsciiHexDigitLower);

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
