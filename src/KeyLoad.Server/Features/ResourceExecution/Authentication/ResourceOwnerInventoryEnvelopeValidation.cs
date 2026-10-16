using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Checks only bounded envelope shape and original request correlation, never owner completeness.</summary>
internal static class ResourceOwnerInventoryEnvelopeValidation
{
    private const int Empty = 0;
    private const long EmptyBytes = 0;
    private const long MinimumPolicyEpoch = 0;

    internal static void RequireRequest(ResourceOwnerInventoryNativeRequest request,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        if (request is null || request.Version != ResourceOwnerInventoryNativeProtocol.Version
            || request.RequestId == Guid.Empty || request.Nonce == Guid.Empty
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
        if (request.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        }
    }

    internal static void RequireReply(ResourceOwnerInventoryNativeRequest request,
        ResourceOwnerInventoryNativeReply reply, IOptions<DatabaseLimits> limits, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        if (request is null || reply is null || request.Version != ResourceOwnerInventoryNativeProtocol.Version
            || reply.Version != request.Version || request.RequestId == Guid.Empty || request.Nonce == Guid.Empty
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
            if (!Enum.IsDefined(error) || !reply.Payload.IsEmpty || reply.SafeDetail != ResourceOwnerInventoryNativeProtocol.Unavailable)
            {
                throw Invalid();
            }
        }
        else if (reply.SafeDetail is not null || reply.Payload.IsEmpty || reply.Payload.Length > request.MaximumResultBytes)
        {
            throw Invalid();
        }
        if (request.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.InvalidProof);
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
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
}
