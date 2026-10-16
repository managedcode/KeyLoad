using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class ControlledBlobOutcomeReadExecutionV2
{
    internal static ControlledBlobReadResult Execute(DatabaseEngine database, IOptions<DatabaseLimits> limits,
        TimeProvider clock, PrincipalRecord principal, GrainRequestEnvelope envelope,
        ControlledBlobReadRequestV2 request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        if (!ControlledBlobOutcomeReadScopeV2.Validate(envelope) || request is null || request.Frame is null || request.Frame.Original is null
            || request.Frame.Original.Purpose != ControlledBlobReadPurpose.Outcome
            || request.Frame.Original.ExpiresAt != envelope.ExpiresAt
            || request.MaximumReadBytes <= ControlledBlobReadProtocol.EmptyBytes
            || request.MaximumReadBytes > limits.Value.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= ControlledBlobReadProtocol.EmptyRecords
            || request.MaximumExaminedRecords > limits.Value.MaxScanRecords
            || request.MaximumResultBytes <= ControlledBlobReadProtocol.EmptyBytes
            || request.MaximumResultBytes > limits.Value.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainResultBytes(request.MaximumResultBytes);
        var grant = work.CreateReadGrant(request.MaximumReadBytes, request.MaximumExaminedRecords);
        var value = database.ReadControlledBlobOutcomeV2Bound(principal.Id, request.Frame, work, grant, binding, cancellationToken);
        if (value is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        var result = new ControlledBlobReadResult(null, true, grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
