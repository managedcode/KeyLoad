using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Uses the actual authentication capability and its fresh persisted checks under one original target budget.</summary>
internal static class NativeAdministratorAuthenticationRead
{
    internal static NativeAdministratorAuthenticationResultV1 Execute(DatabaseEngine database, TimeProvider clock,
        IOptions<GrainRoutingOptions> routing, DecodedGrainRequest request, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        work.AttachResourceLifetimeBinding(binding);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        if (!NativeAdministratorAuthenticationScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var input = GrainNativePayload.Read<NativeAdministratorAuthenticationRequestV1>(request.Payload);
        Require(input, request.Envelope.ExpiresAt, database.Limits);
        work.ConstrainNativeWork(input.MaximumReadBytes, input.MaximumExaminedRecords);
        work.ConstrainResultBytes(input.MaximumResultBytes);
        binding.Bind(ImmutableArray<ResourceLifetimeV1>.Empty);
        ReadOnlyMemory<byte> principalPayload = default;
        PrincipalRecord? principal = null;
        KeyLoadException? refusal = null;
        try
        {
            principal = database.Authenticate(input.Secret, clock.GetUtcNow(), work);
            RuntimeJournalIdentity.RequirePublicCredential(principal.Id);
            GrainRequestAuthority.RequireAdministrator(principal);
        }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            refusal = error;
        }
        if (refusal is null)
        {
            principalPayload = NativeAdministratorAuthenticationEncoding.Principal(principal!, work, routing, token);
        }
        var reply = new NativeAdministratorAuthenticationResultV1(principalPayload, work.ReadBytes,
            work.ConstrainedNativeRecords, refusal?.Code, refusal?.Message);
        _ = ResultByteCounterStream.MeasureNative(work, new GrainValue(reply));
        work.Check();
        return reply;
    }

    private static void Require(NativeAdministratorAuthenticationRequestV1 input, DateTimeOffset expiry, DatabaseLimits limits)
    {
        if (string.IsNullOrEmpty(input.Secret) || input.ExpiresAt != expiry
            || input.MaximumReadBytes <= 0 || input.MaximumReadBytes > limits.MaxQueryReadBytes
            || input.MaximumExaminedRecords <= 0 || input.MaximumExaminedRecords > limits.MaxScanRecords
            || input.MaximumResultBytes <= 0 || input.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
