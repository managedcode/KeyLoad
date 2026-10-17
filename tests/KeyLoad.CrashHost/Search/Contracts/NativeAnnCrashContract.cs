namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeAnnCrashContract
{
    internal const string Prepare = "native-ann-pending-prepare";
    internal const string Fault = "native-ann-checkpoint-fault";
    internal const string Recover = "native-ann-pending-recover";
    internal const string Verify = "native-ann-cold-verify";
    internal const string RequestFile = "native-ann-request.bin";
    internal const string IntentFile = "native-ann-intent.bin";
    internal const string PreparedFile = "native-ann-prepared.json";
    internal const string RecoveredFile = "native-ann-recovered.json";
    internal const string HealthyFile = "native-ann-healthy.json";
    internal const string VerifiedFile = "native-ann-verified.json";
    internal const string Collection = "ann-process-vectors";
    internal const string Field = "/embedding";
    internal const string Consumer = "ann-process-consumer";
    internal const string Model = "ann-process-model";
    internal const string ModelVersion = "v1";
    internal const string SpaceId = "ann-process-space";
    internal const string Invalid = "The native ANN process state differs from its literal fixture.";
    internal const int Count = 3;
    internal const int Dimension = 3;
    internal const int PageLimit = 1;
    internal const int PageBytes = 4_194_304;
    internal const int MaximumPages = 12;
    internal const int SnapshotBound = 512;
    internal const long Generation = 1;
    internal const long Revision = 1;
    internal const int Version = 1;
    internal static PartitionRef Partition { get; } = SampleRetentionCrashScenario.Partition;
    internal static VectorSpace Space { get; } = new(SpaceId, Dimension, DistanceMetric.Cosine, Model, ModelVersion);
    internal static string[] Modes { get; } = [Prepare, Fault, Recover, Verify];
    private const string DocumentPrefix = "ann-process-";
    private const string DocumentIndexFormat = "D2";
    internal static string Id(int index) => DocumentPrefix + index.ToString(DocumentIndexFormat, System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record NativeAnnCrashSnapshot(VectorRecord[] Records, AnnSourceCut Source,
    string IndexSha256, string Receipt, string CanonicalImage, long Position);
