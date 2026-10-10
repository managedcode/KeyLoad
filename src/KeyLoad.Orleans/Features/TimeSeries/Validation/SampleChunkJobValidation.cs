using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.Orleans;

internal static class SampleChunkJobValidation
{
    internal static void Require(SampleChunkWorkHint hint, string partition)
    {
        ArgumentNullException.ThrowIfNull(hint);
        ArgumentNullException.ThrowIfNull(hint.Partition);
        DatabaseEngine.ValidatePartition(hint.Partition);
        JsonData.Identifier(hint.Set);
        JsonData.Identifier(hint.Series);
        JsonData.Identifier(hint.Creator);
        if (hint.Partition.AtomicPartitionId != partition || hint.WindowId == Guid.Empty
            || hint.Revision < SampleChunkLifecycleProtocol.First || hint.Generation < SampleChunkLifecycleProtocol.Absent
            || hint.CreatorPolicyEpoch < SampleChunkLifecycleProtocol.First
            || hint.CommandId != SampleChunkJobIdentity.CommandId(hint.Partition, hint.Set, hint.Series,
                hint.WindowId, hint.Generation, hint.Revision, hint.Seal, hint.CreatorPolicyEpoch))
        { throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid); }
    }
}
