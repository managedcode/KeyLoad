namespace KeyLoad.Core.TimeSeries;

/// <summary>Carries a persisted chunk-window identity and its original idempotent command.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkWorkHint.SerializerAlias)]
public sealed record SampleChunkWorkHint([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string Series,
    [property: Orleans.Id(3)] Guid WindowId, [property: Orleans.Id(4)] long Generation,
    [property: Orleans.Id(5)] long Revision, [property: Orleans.Id(6)] string Creator,
    [property: Orleans.Id(7)] bool Seal, [property: Orleans.Id(8)] Guid CommandId,
    [property: Orleans.Id(9)] long CreatorPolicyEpoch)
{
    internal const string SerializerAlias = "keyload.core.sample-chunk-work-hint";
}
