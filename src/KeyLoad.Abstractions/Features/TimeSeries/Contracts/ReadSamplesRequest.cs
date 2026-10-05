namespace KeyLoad;

/// <summary>Describes a bounded read of samples in a time range.</summary>
/// <param name="Partition">The partition containing the sample set.</param>
/// <param name="Set">The sample set name.</param>
/// <param name="SeriesId">The series identifier.</param>
/// <param name="From">The inclusive beginning of the requested time range.</param>
/// <param name="Until">The inclusive end of the requested UTC time range.</param>
/// <param name="Limit">The maximum number of samples to return.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadSamplesRequest)]
public sealed record ReadSamplesRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId, [property: Orleans.Id(3)] DateTimeOffset From, [property: Orleans.Id(4)] DateTimeOffset Until, [property: Orleans.Id(5)] int Limit = ReadSamplesRequest.DefaultLimit)
{
    private const int DefaultLimit = 1_000;
}
