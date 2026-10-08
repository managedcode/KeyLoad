using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class ControlledDocumentReadExecution
{
    private const long EmptyBytes = 0;
    private const int EmptyRecords = 0;

    internal static ControlledDocumentReadResult Execute(DatabaseEngine database,
        IOptions<DatabaseLimits> limits, TimeProvider clock, PrincipalRecord principal,
        GrainRequestEnvelope envelope, ControlledDocumentReadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        if (!ControlledDocumentReadScope.Validate(envelope) || request.Frame is null
            || request.Frame.ExpiresAt != envelope.ExpiresAt
            || request.MaximumReadBytes <= EmptyBytes || request.MaximumReadBytes > limits.Value.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= EmptyRecords || request.MaximumExaminedRecords > limits.Value.MaxScanRecords
            || request.MaximumResultBytes <= EmptyBytes || request.MaximumResultBytes > limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainResultBytes(request.MaximumResultBytes);
        var grant = work.CreateReadGrant(request.MaximumReadBytes, request.MaximumExaminedRecords);
        var document = database.ReadControlledDocument(principal.Id, request.Frame, work, grant, cancellationToken);
        var result = new ControlledDocumentReadResult(document, grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
