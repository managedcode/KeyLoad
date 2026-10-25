namespace KeyLoad.Core.TimeSeries;

internal static class SampleReadKeys
{
    private const string SampleKeySpace = "sample";
    private const int NextTimestampTick = 1;

    internal static byte[] Prefix(PartitionRef partition, string set, string seriesId)
        => KeySpace.Partition(SampleKeySpace, partition, set, seriesId);

    internal static byte[] FromInclusive(PartitionRef partition, string set, string seriesId,
        DateTimeOffset from)
        => PrefixTimestamp(partition, set, seriesId, from);

    internal static global::ManagedCode.Communication.Result<byte[]?> UntilExclusive(PartitionRef partition, string set, string seriesId,
        DateTimeOffset? untilExclusive)
        => ((byte[]?)(untilExclusive is null ? null : PrefixTimestamp(partition, set, seriesId, untilExclusive.Value))) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<byte[]?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<byte[]?>.FailNotFound(nameof(UntilExclusive));

    internal static global::ManagedCode.Communication.Result<byte[]?> ThroughInclusive(PartitionRef partition, string set, string seriesId,
        DateTimeOffset? atOrBefore)
    {
        if (atOrBefore is null || atOrBefore.Value.UtcTicks == DateTimeOffset.MaxValue.UtcTicks)
        {
            return global::ManagedCode.Communication.Result<byte[]?>.FailNotFound(nameof(ThroughInclusive));
        }

        return global::ManagedCode.Communication.Result<byte[]?>.Succeed((PrefixTimestamp(partition, set, seriesId,
            new DateTimeOffset(atOrBefore.Value.UtcTicks + NextTimestampTick, TimeSpan.Zero))));
    }

    private static byte[] PrefixTimestamp(PartitionRef partition, string set, string seriesId,
        DateTimeOffset timestamp)
        => KeySpace.Partition(SampleKeySpace, partition, set, seriesId, timestamp);
}
