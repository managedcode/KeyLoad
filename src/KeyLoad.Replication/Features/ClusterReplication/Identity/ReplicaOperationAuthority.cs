using KeyLoad.Core;

namespace KeyLoad.Replication;

internal static class ReplicaOperationAuthority
{
    internal const string Required = "A canonical database authority is required for replica operations.";
    private const string MissingNativeBody = "The replica operation has no authenticated native body.";

    internal static ReplicatedOperation? Verify(ReplicatedOperation? operation, DatabaseEngine? database)
    {
        if (operation is null)
        {
            return null;
        }
        var canonical = database ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Required);
        if (operation.NativePayload.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingNativeBody);
        }
        return canonical.VerifyOperationAuthority(operation);
    }

    internal static ReplicaEntry Own(ReplicaEntry entry, DatabaseEngine? database)
        => entry.Operation is null ? entry : entry with { Operation = Verify(entry.Operation, database) };

    internal static bool Equal(ReplicaEntry left, ReplicaEntry right, DatabaseEngine? database)
    {
        if (left.Index != right.Index || left.Term != right.Term)
        {
            return false;
        }
        if (left.Operation is null || right.Operation is null)
        {
            return left.Operation is null && right.Operation is null;
        }
        var canonical = database ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Required);
        return canonical.NativeOperationsEqual(left.Operation, right.Operation);
    }
}
