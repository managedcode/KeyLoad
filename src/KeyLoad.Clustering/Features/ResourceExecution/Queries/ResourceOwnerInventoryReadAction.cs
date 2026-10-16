using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class ResourceOwnerInventoryReadAction
{
    private const long NoReadBytes = 0;
    private const int NoExaminedRecords = 0;

    internal static ResourceOwnerInventoryReadReplyV1 Execute(DatabaseEngine database, string principalId,
        ResourceOwnerInventoryReadRequestV1 query, ReadExecutionBudget work)
    {
        switch (query.Operation)
        {
            case ResourceOwnerInventoryReadOperation.CaptureControl:
                return new(database.CaptureResourceOwnerInventoryControl(principalId, query.Request, work),
                    null, false, NoReadBytes, NoExaminedRecords);
            case ResourceOwnerInventoryReadOperation.ReadPage:
                return new(null, database.ReadResourceOwnerInventoryPage(principalId, query.Control!,
                    query.ObservedOwner, query.Continuation, work), false, NoReadBytes, NoExaminedRecords);
            case ResourceOwnerInventoryReadOperation.ValidateControl:
                database.ValidateResourceOwnerInventoryControl(principalId, query.Control!, work);
                break;
            case ResourceOwnerInventoryReadOperation.ValidateOwner:
                database.ValidateResourceOwnerInventoryOwner(principalId, query.Control!, query.OwnerCut!, work);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryReadScope.InvalidRequest);
        }
        return new(null, null, true, NoReadBytes, NoExaminedRecords);
    }
}
