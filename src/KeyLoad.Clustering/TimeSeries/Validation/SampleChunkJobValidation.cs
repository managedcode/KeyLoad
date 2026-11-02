using KeyLoad.Core;
using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Clustering;

internal static class SampleChunkJobValidation
{
    internal static void Require(SampleChunkWorkHint hint, string partition)
    {
        ArgumentNullException.ThrowIfNull(hint) /* KLD0041: BCL null validation accepts the hint SampleChunkWorkHint reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(hint.Partition) /* KLD0041: BCL null validation accepts the hint.Partition PartitionRef reference through its object parameter. */;
        EngineResourceAccess.ValidatePartition(hint.Partition);
        JsonData.Identifier(hint.Set);
        JsonData.Identifier(hint.Series);
        JsonData.Identifier(hint.Creator);
        if (hint.Partition.AtomicPartitionId != partition || hint.WindowId == Guid.Empty
            || hint.Revision < SampleChunkLifecycleProtocol.First || hint.Generation < SampleChunkLifecycleProtocol.Absent
            || hint.CreatorPolicyEpoch < SampleChunkLifecycleProtocol.First
            || hint.CommandId != SampleChunkJobIdentity.CommandId(hint.Partition, hint.Set, hint.Series,
                hint.WindowId, hint.Generation, hint.Revision, hint.Seal, hint.CreatorPolicyEpoch))
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid);
        }
    }
}
