using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionControlCommandStorage
{
    internal static PartitionControlCommandRecord? Read(IKeyValueView view,
        PartitionControlCommandIdentity identity, int maximumBytes)
    {
        PartitionControlCommandRecord? record = null;
        view.ReadValue(PartitionControlCommandKeys.Authority(identity), bytes =>
        {
            if (bytes.Length > maximumBytes)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
            record = NativeSerialization.Deserialize<PartitionControlCommandRecord>(bytes);
            PartitionControlCommandValidation.Require(record, identity);
        });
        return record;
    }

    internal static void Write(IAtomicTransaction transaction, PartitionControlCommandRecord record,
        int maximumBytes)
    {
        PartitionControlCommandValidation.Require(record, record.Identity);
        if (NativeSerialization.Measure(record) > maximumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        transaction.Put(PartitionControlCommandKeys.Authority(record.Identity), NativeSerialization.Serialize(record));
    }
}
