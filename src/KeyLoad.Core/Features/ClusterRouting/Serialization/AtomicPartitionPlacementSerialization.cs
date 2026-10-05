using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class AtomicPartitionPlacementSerialization
{
    private const string DirectoryKeySpace = "atomic-partition-placement-directory";
    private const string DirectoryVersion = "v1";
    private const string RowKeySpace = "atomic-partition-placement";
    private const string RowVersion = "v1";
    private const string Malformed = "The committed atomic partition placement is malformed.";

    internal static byte[] DirectoryKey() => KeyCodec.Encode(DirectoryKeySpace, DirectoryVersion);

    internal static byte[] RowKey(PartitionRef partition)
        => KeyCodec.Encode(RowKeySpace, RowVersion, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey);

    internal static AtomicPartitionPlacementDirectoryV1? ReadDirectory(IKeyValueView view)
        => Read<AtomicPartitionPlacementDirectoryV1>(view, DirectoryKey());

    internal static AtomicPartitionPlacementV1? ReadRow(IKeyValueView view, PartitionRef partition)
        => Read<AtomicPartitionPlacementV1>(view, RowKey(partition));

    internal static AtomicPartitionPlacementDirectoryV1? ReadDirectory(IKeyValueView view,
        ReadExecutionBudgetReadGrant grant) => Read<AtomicPartitionPlacementDirectoryV1>(view, DirectoryKey(), grant);

    internal static AtomicPartitionPlacementV1? ReadRow(IKeyValueView view, PartitionRef partition,
        ReadExecutionBudgetReadGrant grant) => Read<AtomicPartitionPlacementV1>(view, RowKey(partition), grant);

    internal static T? Read<T>(IKeyValueView view, byte[] key) where T : class
    {
        T? record = null;
        view.ReadValue(key, bytes => record = Deserialize<T>(bytes));
        return record;
    }

    internal static T? Read<T>(IKeyValueView view, byte[] key, ReadExecutionBudgetReadGrant grant)
        where T : class
    {
        T? record = null;
        grant.ReadValue(view, key, bytes => record = Deserialize<T>(bytes));
        return record;
    }

    internal static byte[] SerializeBounded<T>(T record)
    {
        var bytes = NativeSerialization.Serialize(record);
        if (bytes.Length > AtomicPartitionPlacementProtocol.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The atomic partition placement exceeds its encoded byte limit.");
        }

        return bytes;
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : class
    {
        if (bytes.Length > AtomicPartitionPlacementProtocol.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, Malformed);
        }

        try
        {
            return NativeSerialization.Deserialize<T>(bytes);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Corruption, Malformed);
        }
    }
}
