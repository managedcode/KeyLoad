namespace KeyLoad.Orleans;

internal static class SampleChunkJobProtocol
{
    internal const string GrainAlias = "keyload.orleans.sample-chunk-coordinator.v1";
    internal const string Admissions = "time-series-chunk-admissions-v1";
    internal const string JobName = "keyload-sample-chunk-v1";
    internal const string HintKey = "hint.v1";
    internal const int MetadataCount = 1;
    internal const int CoordinatorInterfaceVersion = 1;
    internal const string Invalid = "The native chunk job identity is invalid.";
    internal const string Unresolved = "The original chunk scheduling admission remains unresolved.";
    internal const string Exhausted = "The native chunk job admission capacity is exhausted.";
}
