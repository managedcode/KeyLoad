namespace KeyLoad.Clustering;

internal static class SampleChunkJobProtocol
{
    internal const string GrainAlias = "keyload.orleans.sample-chunk-coordinator";
    internal const string Admissions = "time-series-chunk-admissions";
    internal const string JobName = "keyload-sample-chunk";
    internal const string HintKey = "hint";
    internal const int MetadataCount = 1;
    internal const int CoordinatorInterfaceVersion = 1;
    internal const string Invalid = "The execution chunk job identity is invalid.";
    internal const string Unresolved = "The original chunk scheduling admission remains unresolved.";
    internal const string Exhausted = "The execution chunk job admission capacity is exhausted.";
}
