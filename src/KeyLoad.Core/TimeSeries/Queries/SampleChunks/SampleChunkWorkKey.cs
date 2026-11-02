using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleChunkWorkKey
{
    private const int ComponentCount = 8;
    private const int DatabaseIndex = 2;
    private const int DomainIndex = 3;
    private const int PartitionIndex = 4;
    private const int SetIndex = 5;
    private const int SeriesIndex = 6;
    private const int WindowIndex = 7;
    private const string GuidFormat = "N";

    internal static (PartitionRef Partition, string Set, string Series, Guid Id) Decode(ReadOnlySpan<byte> key)
    {
        var parts = KeyCodec.Decode(key);
        if (parts.Length != ComponentCount || parts[SampleChunkLifecycleProtocol.FirstIndex] is not { Kind: KeyComponentKind.Text, Text: var family }
            || family != PartitionRecordFamilies.SampleChunkWindow
            || parts[1] is not { Kind: KeyComponentKind.Text, Text: var tenant } || parts[DatabaseIndex] is not { Kind: KeyComponentKind.Text, Text: var database }
            || parts[DomainIndex] is not { Kind: KeyComponentKind.Text, Text: var domain } || parts[PartitionIndex] is not { Kind: KeyComponentKind.Text, Text: var partition }
            || parts[SetIndex] is not { Kind: KeyComponentKind.Text, Text: var set } || parts[SeriesIndex] is not { Kind: KeyComponentKind.Text, Text: var series }
            || parts[WindowIndex] is not { Kind: KeyComponentKind.Text, Text: var id } || !Guid.TryParseExact(id, GuidFormat, out var windowId)
            || windowId == Guid.Empty || !KeyCodec.Encode(parts).AsSpan().SequenceEqual(key))
        {
            throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt);
        }
        var scope = new PartitionRef(tenant, database, domain, partition);
        EngineResourceAccess.ValidatePartition(scope);
        JsonData.Identifier(set);
        JsonData.Identifier(series);
        return (scope, set, series, windowId);
    }
}
