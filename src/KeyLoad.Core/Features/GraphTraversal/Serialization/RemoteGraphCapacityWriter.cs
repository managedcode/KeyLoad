using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class RemoteGraphCapacityWriter
{
    private const long Empty = 0;
    private const long Added = 1;
    private const long Removed = -1;
    private const int ExistenceProbe = 1;

    internal static void Apply(IAtomicTransaction tx, PartitionRef partition, string direction,
        byte[] prefix, IReadOnlyList<GraphCrossPartitionRecordWrite> writes, DatabaseLimits limits)
    {
        var key = RemoteGraphKeys.Capacity(partition, direction);
        var current = GraphCrossPartitionRecords.Read<RemoteGraphCapacityV1>(tx, key);
        if (current is not null)
        {
            if (current.Version != RemoteGraphProtocol.Version || current.Direction != direction
                || current.RecordCount < Empty || current.EncodedBytes < Empty
                || current.RecordCount > limits.MaxScanRecords || current.EncodedBytes > limits.MaxBatchBytes)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        }
        else if (tx.VisitRange(prefix, ExistenceProbe, static (_, _) => false).Records != Empty)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        var next = Measure(tx, writes, current, limits);
        foreach (var write in writes)
        {
            if (write.Value is null)
            { tx.Delete(write.Key); }
            else
            { tx.Put(write.Key, write.Value); }
        }
        tx.PutRecord(key, new RemoteGraphCapacityV1(RemoteGraphProtocol.Version, direction,
            next.Count, next.Bytes));
    }

    private static (long Count, long Bytes) Measure(IAtomicTransaction tx,
        IReadOnlyList<GraphCrossPartitionRecordWrite> writes, RemoteGraphCapacityV1? current,
        DatabaseLimits limits)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var count = current?.RecordCount ?? Empty;
        var bytes = current?.EncodedBytes ?? Empty;
        try
        {
            foreach (var write in writes)
            {
                if (!keys.Add(Convert.ToHexString(write.Key)))
                { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
                var delta = MeasureWrite(tx, write, limits);
                count = checked(count + delta.Count);
                bytes = checked(bytes + delta.Bytes);
            }
        }
        catch (OverflowException)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        if (count < Empty || bytes < Empty)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        if (count > limits.MaxScanRecords || bytes > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphProtocol.Invalid); }
        return (count, bytes);
    }

    private static (long Count, long Bytes) MeasureWrite(IAtomicTransaction tx,
        GraphCrossPartitionRecordWrite write, DatabaseLimits limits)
    {
        int? oldLength = null;
        tx.ReadValue(write.Key, value => oldLength = value.Length);
        if (write.Value is { Length: var length } && length > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphProtocol.Invalid); }
        var count = oldLength.HasValue ? write.Value is null ? Removed : Empty
            : write.Value is null ? Empty : Added;
        var bytes = checked((write.Value?.LongLength ?? Empty) - oldLength.GetValueOrDefault());
        return (count, bytes);
    }
}
