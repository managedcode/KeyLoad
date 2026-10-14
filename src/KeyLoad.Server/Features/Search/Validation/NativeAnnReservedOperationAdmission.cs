namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnReservedOperationAdmission
{
    internal static void Require(ProjectionConsumerInfo configured, NativeAnnOwnedKey key,
        NativeAnnOriginalOperation? entry, AnnMaintenanceRequest request)
    {
        if (configured.InitialAnnMaintenance is { } reserved)
        {
            NativeAnnOriginalResults.RequireRequest(key, reserved);
            if (request.CommandId == reserved.CommandId)
            {
                if (!NativeAnnOriginalResults.SameRequest(reserved, request))
                {
                    throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.InvalidSource);
                }
                if (entry is null && configured.InitialAnnMaintenanceSettled)
                {
                    throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
                }
            }
            if (!configured.InitialAnnMaintenanceSettled && !NativeAnnOriginalResults.SameRequest(reserved, request))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted);
            }
            if (entry is not null && !configured.InitialAnnMaintenanceSettled
                && !NativeAnnOriginalResults.SameRequest(reserved, entry.InitialRequest))
            {
                throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
            }
        }
    }
}
