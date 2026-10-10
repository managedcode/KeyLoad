using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWorkKey
{
    private const int ComponentCount = 8;
    private const int TenantIndex = 1;
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
        if (parts.Length != ComponentCount || parts[SampleChunkLifecycleProtocol.FirstIndex] is not string family
            || family != PartitionRecordFamilies.SampleChunkWindow
            || parts[TenantIndex] is not string tenant || parts[DatabaseIndex] is not string database
            || parts[DomainIndex] is not string domain || parts[PartitionIndex] is not string partition
            || parts[SetIndex] is not string set || parts[SeriesIndex] is not string series
            || parts[WindowIndex] is not string id || !Guid.TryParseExact(id, GuidFormat, out var windowId)
            || windowId == Guid.Empty || !KeyCodec.Encode(parts).AsSpan().SequenceEqual(key))
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        var scope = new PartitionRef(tenant, database, domain, partition);
        DatabaseEngine.ValidatePartition(scope);
        JsonData.Identifier(set);
        JsonData.Identifier(series);
        return (scope, set, series, windowId);
    }
}
