namespace KeyLoad.Core;

internal static class ClusterBackupRequestValidation
{
    private const string Invalid = "The cluster owner backup request is invalid.";

    internal static void Require(ClusterBackupOwnerRequest? request)
    {
        if (request is null || request.Version != ClusterBackupOwnerRequest.CurrentVersion
            || request.CaptureId == Guid.Empty || request.ExpectedNodeId == Guid.Empty || request.ExpectedOwner is null
            || request.ExpectedOwner.VoterIds.IsDefault)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
    }
}
