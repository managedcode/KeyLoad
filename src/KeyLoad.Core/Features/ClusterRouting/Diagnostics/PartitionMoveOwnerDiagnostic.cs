using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core;

internal static class PartitionMoveOwnerDiagnostic
{
    internal const string MetadataKey = "KeyLoad.Core.ClusterRouting.MoveOwnerGuard";

    internal static KeyLoadException Fail(PartitionMoveOwnerGuard guard)
    {
        var error = Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        if (guard != PartitionMoveOwnerGuard.Unobserved && Enum.IsDefined(guard))
        {
            error.Data[MetadataKey] = (int)guard;
        }
        return error;
    }

    internal static PartitionMoveOwnerGuard Read(Exception error)
    {
        if (error is not KeyLoadException domain || domain.Code != ErrorCode.OwnershipLost
            || domain.Message != PartitionMoveProtocol.OwnerMismatch
            || error.Data[MetadataKey] is not int value)
        {
            return PartitionMoveOwnerGuard.Unobserved;
        }
        var guard = (PartitionMoveOwnerGuard)value;
        return Enum.IsDefined(guard) ? guard : PartitionMoveOwnerGuard.Unobserved;
    }
}
