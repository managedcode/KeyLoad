using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferWireRecordInventory(DatabaseEngine owner, CancellationToken token)
{
    private readonly List<byte[][]> rows = [];
    private int examined;
    private long bytes;
    internal byte[][][] Rows => rows.ToArray();

    internal void Read(IKeyValueView view, PartitionRef partition)
    {
        foreach (var family in PartitionRecordFamilies.All)
        {
            token.ThrowIfCancellationRequested();
            var result = view.VisitRange(KeySpace.Partition(family, partition), owner.Limits.MaxScanRecords,
                Copy, observer: Charge, cancellationToken: token);
            if (result.HasMore || result.StoppedByVisitor)
            { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        }
    }

    private void Charge(long actual)
    {
        examined = checked(examined + 1);
        bytes = checked(bytes + actual);
        if (examined > owner.Limits.MaxScanRecords || bytes > owner.Limits.MaxQueryReadBytes)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
    }

    private bool Copy(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        rows.Add([key.ToArray(), value.ToArray()]);
        return true;
    }
}
