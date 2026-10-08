using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ImmutableArray<KeyValueRecord> ReadMoveGrantIndex(IKeyValueView view, PartitionMoveControlRecord control)
    {
        var page = view.Scan(PartitionMoveGrantStorage.MovePrefix(control.Partition, control.MoveId), Limits.MaxScanRecords);
        if (page.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        long bytes = PartitionMoveProtocol.EmptyCount;
        foreach (var row in page.Records)
        {
            bytes = checked(bytes + row.Key.Length + row.Value.Length);
            if (bytes > Limits.MaxQueryReadBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        }
        return page.Records;
    }

    private PartitionMovePhaseGrant ReadIndexedMoveGrant(IKeyValueView view,
        PartitionMoveControlRecord control, KeyValueRecord row)
    {
        var id = NativeSerialization.Deserialize<Guid>(row.Value.Span);
        var grant = PartitionMoveGrantStorage.Read(view, control.Partition, id, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (id == Guid.Empty || grant.GrantId != id || grant.MoveId != control.MoveId
            || grant.Partition != control.Partition || grant.OperatorPrincipalId != control.PrincipalId
            || !row.Key.Span.SequenceEqual(PartitionMoveGrantStorage.MoveKey(control.Partition, control.MoveId, id)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        return grant;
    }
}
