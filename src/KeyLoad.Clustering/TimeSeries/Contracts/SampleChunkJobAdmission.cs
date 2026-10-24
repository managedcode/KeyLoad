using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Clustering;

[GenerateSerializer, Alias(SampleChunkJobAdmission.SerializerAlias)]
internal sealed record SampleChunkJobAdmission([property: Id(0)] SampleChunkWorkHint Hint,
    [property: Id(1)] string? JobId, [property: Id(2)] long? BlockedPolicyEpoch,
    [property: Id(3)] ErrorCode? SettledError)
{
    internal const string SerializerAlias = "keyload.orleans.sample-chunk-job-admission";
}
