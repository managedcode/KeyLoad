namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveData
{
    internal const string Root = "root";
    internal const string Credential = "root.movement-native-fixture-credential-32-characters";
    internal const string Secondary = "d2-archive-existing-admin";
    internal const string SecondaryCredential = "d2-archive-existing-native-credential-32-characters";
    internal const string DirectoryPrefix = "keyload-d2-archive-";
    internal const string GuidFormat = "N";
    internal const string SeedId = "original";
    internal const string UnrelatedId = "unrelated";
    internal const string HealthyId = "restored";
    internal const string SeedJson = "{\"archived\":true}";
    internal const string UnrelatedJson = "{\"unrelated\":true}";
    internal const string HealthyJson = "{\"restored\":true}";
    internal const string PutDocumentKind = "putDocument";
    internal const int Version = 1;
    internal const int Empty = 0;
    internal const int Single = 1;
    internal const int OwnerCount = 2;
    internal const int SeedEntries = 5;
    internal const int SigningKeyBytes = 32;
    internal const int TwoCold = 2;
    internal const int RestoreCommits = 2;
    internal const int InvalidOriginVersion = 0;
    internal const string StageSuffix = ".slot-stage";
    internal static readonly string[] ArchiveFiles = ["backup.json", "catalog-backup.native", "commands.wal", "identity.json"];
    internal static PartitionRef Partition(int owner) => new("d2-archive", "closure", "domain-" + owner, "partition-" + owner);
    internal static string Documents(int owner) => "archive-documents-" + owner;
    internal static string Unrelated(int owner) => "archive-unrelated-" + owner;
    internal static string[] TargetVoters(int owner) => owner == Empty
        ? ["http://127.0.0.1:28001", "http://127.0.0.1:28002", "http://127.0.0.1:28003"]
        : ["http://127.0.0.1:28004", "http://127.0.0.1:28005", "http://127.0.0.1:28006"];
}
