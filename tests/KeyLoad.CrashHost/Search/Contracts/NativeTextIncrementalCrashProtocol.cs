namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalCrashProtocol
{
    private const string Tenant = "text-crash-tenant";
    private const string Database = "text-crash-database";
    private const string Domain = "text";
    private const string PartitionKey = "partition";
    internal const string Prepare = "native-text-incremental-prepare";
    internal const string Fault = "native-text-incremental-fault";
    internal const string Recover = "native-text-incremental-recover";
    internal const string Verify = "native-text-incremental-verify";
    internal const string RequestFile = "text-incremental-request.bin";
    internal const string OriginalFile = "text-incremental-original.bin";
    internal const string RecoveredFile = "text-incremental-recovered.bin";
    internal const string HealthyFile = "text-incremental-healthy.bin";
    internal const string VerifiedFile = "text-incremental-verified.bin";
    internal const string CheckpointCommandPrefix = "text-incremental-checkpoint-command-";
    internal const string CheckpointReceiptPrefix = "text-incremental-checkpoint-receipt-";
    internal const string EvidenceSuffix = ".bin";
    internal const string EvidenceIdFormat = "N";
    internal const string Collection = "text-incremental";
    internal const string Consumer = "text-incremental-consumer";
    internal const string Field = "/text";
    internal const string Ukrainian = "ukrainian";
    internal const string English = "english";
    internal const string InitialUkrainianJson = """{"text":"привіт світ"}""";
    internal const string InitialEnglishJson = """{"text":"hello world"}""";
    internal const string ChangedJson = """{"text":"оновлено changed"}""";
    internal const string HealthyJson = """{"text":"продовжено healthy"}""";
    internal const string Invalid = "The native incremental text process evidence is inconsistent.";
    internal const string OriginalAlias = "keyload.crashhost.text-incremental-original.v1";
    internal const string ResultAlias = "keyload.crashhost.text-incremental-result.v1";
    internal const long Generation = 1;
    internal const long FirstTerm = 1;
    internal const long Revision = 1;
    internal const long ChangedRevision = 2;
    internal const long HealthyRevision = 3;
    internal const int PlacementVersion = 1;
    internal const int MaximumPages = 4;
    internal static PartitionRef Partition { get; } = new(Tenant, Database, Domain, PartitionKey);
}
