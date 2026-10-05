using System.Globalization;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal static class PartitionRecordCancellationSeed
{
    internal const string Value = "partition-record-cancellation-value";
    internal const string SuffixFormat = "D6";
    internal const int RecordCount = 50_000;

    internal static (byte[] Key, byte[] Value)[] Create(PartitionRef partition, string family)
        => Enumerable.Range(0, RecordCount)
            .Select(index => PartitionRecordNativeFixture.Row(family, partition,
                index.ToString(SuffixFormat, CultureInfo.InvariantCulture), Value))
            .ToArray();
}
