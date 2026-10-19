using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.RuntimeJournaling.Identity;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Uses the actual authentication capability and its fresh persisted checks under one original target budget.</summary>
internal static class AdministratorAuthenticationRead
{
    internal static AdministratorAuthenticationResult Execute(DatabaseEngine database, TimeProvider clock,
        IOptions<GrainRoutingOptions> routing, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        work.AttachResourceLifetimeBinding(binding);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        if (!AdministratorAuthenticationScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var input = GrainPayload.Read<AdministratorAuthenticationRequest>(request.Payload);
        Require(input, request.Envelope.ExpiresAt, database.Limits);
        work.ConstrainWork(input.MaximumReadBytes, input.MaximumExaminedRecords);
        work.ConstrainResultBytes(input.MaximumResultBytes);
        binding.Bind(ImmutableArray<ResourceLifetime>.Empty);
        ReadOnlyMemory<byte> principalPayload = default;
        PrincipalRecord? principal = null;
        KeyLoadException? refusal = null;
        try
        {
            principal = database.Authenticate(input.Secret, clock.GetUtcNow(), work);
            RuntimeJournalIdentity.RequirePublicCredential(principal.Id);
            GrainRequestAuthority.RequireAdministrator(principal);
        }
        catch (KeyLoadException error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            refusal = error;
        }
        if (refusal is null)
        {
            principalPayload = AdministratorAuthenticationEncoding.Principal(principal!, work, routing, token);
        }
        var reply = new AdministratorAuthenticationResult(principalPayload, work.ReadBytes,
            work.ConstrainedRecords, refusal?.Code, refusal?.Message);
        _ = ResultByteCounterStream.RuntimeMeasure(work, new GrainValue(reply));
        work.Check();
        return reply;
    }

    private const int NoReadBytes = 0;
    private const int NoExaminedRecords = 0;
    private const int NoResultBytes = 0;

    private static void Require(AdministratorAuthenticationRequest input, DateTimeOffset expiry, DatabaseLimits limits)
    {
        if (string.IsNullOrEmpty(input.Secret) || input.ExpiresAt != expiry
            || input.MaximumReadBytes <= NoReadBytes || input.MaximumReadBytes > limits.MaxQueryReadBytes
            || input.MaximumExaminedRecords <= NoExaminedRecords || input.MaximumExaminedRecords > limits.MaxScanRecords
            || input.MaximumResultBytes <= NoResultBytes || input.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
