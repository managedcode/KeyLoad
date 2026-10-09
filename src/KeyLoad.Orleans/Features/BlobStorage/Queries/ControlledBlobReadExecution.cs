using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class ControlledBlobReadExecution
{
    internal static ControlledBlobReadResult Execute(DatabaseEngine database, IOptions<DatabaseLimits> limits,
        TimeProvider clock, PrincipalRecord principal, GrainRequestEnvelope envelope,
        ControlledBlobReadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        if (!ControlledBlobReadScope.Validate(envelope) || request.Frame is null
            || request.Frame.ExpiresAt != envelope.ExpiresAt
            || request.MaximumReadBytes <= ControlledBlobReadProtocol.EmptyBytes
            || request.MaximumReadBytes > limits.Value.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= ControlledBlobReadProtocol.EmptyRecords
            || request.MaximumExaminedRecords > limits.Value.MaxScanRecords
            || request.MaximumResultBytes <= ControlledBlobReadProtocol.EmptyBytes
            || request.MaximumResultBytes > limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainResultBytes(request.MaximumResultBytes);
        var grant = work.CreateReadGrant(request.MaximumReadBytes, request.MaximumExaminedRecords);
        var value = database.ReadControlledBlob(principal.Id, request.Frame, work, grant, cancellationToken);
        var result = new ControlledBlobReadResult(value, request.Frame.Purpose == ControlledBlobReadPurpose.Outcome,
            grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
