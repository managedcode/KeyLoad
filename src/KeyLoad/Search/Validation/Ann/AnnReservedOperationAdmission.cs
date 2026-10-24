namespace KeyLoad.Server.Search;

internal static class AnnReservedOperationAdmission
{
    internal static void Require(ProjectionConsumerInfo configured, AnnOwnedKey key,
        AnnOriginalOperation? entry, AnnMaintenanceRequest request)
    {
        if (configured.InitialAnnMaintenance is { } reserved)
        {
            AnnOriginalResults.RequireRequest(key, reserved);
            if (request.CommandId == reserved.CommandId)
            {
                if (!AnnOriginalResults.SameRequest(reserved, request))
                {
                    throw Errors.Fail(ErrorCode.Conflict, AnnProtocol.InvalidSource);
                }
                if (entry is null && configured.InitialAnnMaintenanceSettled)
                {
                    throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
                }
            }
            if (!configured.InitialAnnMaintenanceSettled && !AnnOriginalResults.SameRequest(reserved, request))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
            }
            if (entry is not null && !configured.InitialAnnMaintenanceSettled
                && !AnnOriginalResults.SameRequest(reserved, entry.InitialRequest))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            }
        }
    }
}
