namespace KeyLoad.Orleans;

/// <summary>Dispatches one closed private source capability after the native quorum and principal reload.</summary>
internal static class PartitionMovementCaptureExecution
{
    private const int FirstOrdinal = 0;

    internal static async Task<object?> ExecuteAsync(INativePartitionMovementCapture owner,
        PrincipalRecord principal, PartitionMovementCaptureCapability request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        switch (request.Action)
        {
            case PartitionMovementCaptureAction.Capture:
                if (request.HandleId != Guid.Empty || request.Ordinal != FirstOrdinal)
                { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
                return await owner.CaptureAsync(principal, request.Verified, cancellationToken).ConfigureAwait(true);
            case PartitionMovementCaptureAction.Page:
                RequireHandle(request);
                return await owner.ReadPageAsync(principal, request.Verified,
                    new(request.HandleId, request.Ordinal), cancellationToken).ConfigureAwait(true);
            case PartitionMovementCaptureAction.Release:
                RequireHandle(request);
                if (request.Ordinal != FirstOrdinal)
                { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
                return await owner.ReleaseAsync(principal, request.Verified, request.HandleId, cancellationToken).ConfigureAwait(true);
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private static void RequireHandle(PartitionMovementCaptureCapability request)
    {
        if (request.HandleId == Guid.Empty || request.Ordinal < FirstOrdinal)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
    }
}
