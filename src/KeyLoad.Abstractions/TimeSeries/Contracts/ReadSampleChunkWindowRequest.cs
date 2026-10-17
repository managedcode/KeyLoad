namespace KeyLoad;

/// <summary>Reads one authorized enrolled window with an optional authorized scalar tag equality.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Read)]
public sealed record ReadSampleChunkWindowRequest([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId,
    [property: Orleans.Id(3)] Guid WindowId, [property: Orleans.Id(4)] string? TagPointer,
    [property: Orleans.Id(5)] string? TagValue, [property: Orleans.Id(6)] int Limit)
{
    /// <summary>Optional acknowledged minimum in the same canonical physical placement and incarnation.</summary>
    [Orleans.Id(7)]
    public CommitToken? MinimumToken
    {
        get;
        init;
    }
}
