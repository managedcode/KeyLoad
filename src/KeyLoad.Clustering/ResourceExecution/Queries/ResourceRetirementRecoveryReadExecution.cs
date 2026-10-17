using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementRecoveryReadExecution
{
    internal static ResourceRetirementReadReplyV1 Read(DatabaseEngine database, string principalId,
        ResourceRetirementReadRequestV1 query, ReadExecutionBudget work)
    {
        var result = new ResourceRetirementReadReplyV1(null, null, null,
            ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords);
        switch (query.Operation)
        {
            case ResourceRetirementReadOperation.OriginalWorkSeal:
                return result with
                {
                    State = database.ReadResourceRetirementOriginalWorkSeal(principalId, query.Control!, work)
                };
            case ResourceRetirementReadOperation.OriginalD3Seal:
                return result with
                {
                    State = database.ReadResourceRetirementOriginalSeal(principalId, query.Control!, work)
                };
            case ResourceRetirementReadOperation.DropParent:
                var admission = database.ReadResourceRetirementDropParentOrAdmit(principalId, query.ParentLookup!, query.ExpiresAt, work);
                return result with
                {
                    Parent = admission.Parent,
                    AdmittedDrop = admission.Admitted
                };
            case ResourceRetirementReadOperation.CleanupProgress:
                return query.CleanupPartition is { } partition
                    ? result with
                    {
                        CleanupCheckpoint = database.ReadResourceRetirementCleanupCheckpoint(principalId, query.Control!, partition, work)
                    }
                    : result with
                    {
                        OwnerCleanup = database.ReadResourceRetirementOwnerCleanup(principalId, query.Control!, work)
                    };
            case ResourceRetirementReadOperation.OwnerRelease:
                return result with
                {
                    OwnerRelease = database.ReadResourceRetirementOwnerRelease(principalId, query.Control!, work)
                };
            default:
                throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
    }
}
