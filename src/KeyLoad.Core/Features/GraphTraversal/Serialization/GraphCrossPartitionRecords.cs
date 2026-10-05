using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class GraphCrossPartitionRecords
{
    private const string Malformed = "A committed graph cross-partition record is malformed.";
    private const string CapacityExceeded = "The graph projection capacity is exhausted.";

    internal static T? Read<T>(IKeyValueView view, byte[] key) where T : class
    {
        T? record = null;
        view.ReadValue(key, bytes => record = Decode<T>(bytes));
        return record;
    }

    internal static T? Read<T>(IKeyValueView view, byte[] key, ReadExecutionBudgetReadGrant grant)
        where T : class
    {
        T? record = null;
        grant.ReadValue(view, key, bytes => record = Decode<T>(bytes));
        return record;
    }

    internal static byte[] Encode<T>(T record, int maximumBytes)
    {
        var bytes = NativeSerialization.Serialize(record);
        if (bytes.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CapacityExceeded);
        }
        return bytes;
    }

    internal static string Fingerprint(PartitionRef source, string graph, string edgeId,
        EntityRef destination, long revision, bool deleted, EdgeRecord edge)
    {
        var bytes = NativeSerialization.Serialize(new GraphCrossPartitionFingerprintV1(
            GraphCrossPartitionProtocol.CurrentVersion, source, graph, edgeId, destination,
            revision, deleted, edge));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static T Decode<T>(ReadOnlySpan<byte> bytes) where T : class
    {
        try
        {
            return NativeSerialization.Deserialize<T>(bytes);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Corruption, Malformed);
        }
    }
}
