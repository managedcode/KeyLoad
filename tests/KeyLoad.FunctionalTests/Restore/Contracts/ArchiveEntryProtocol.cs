namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class ArchiveEntryProtocol
{
    internal const string Prefix = "keyload-execution-archive-entry-";
    internal const string GuidFormat = "N";
    internal const string Source = "source";
    internal const string Backup = "backup";
    internal const string Target = "restored";
    internal const string HiddenPrefix = "retained-";
    internal const string Root = "root";
    internal const string Manifest = "backup.json";
    internal const string Identity = "identity.json";
    internal const string Journal = "commands.wal";
    internal const string BackupCommand = "backup";
    internal const string RestoreCommand = "restore";
    internal const string Collection = "_archive_entries";
    internal const string Index = "by-label";
    internal const string LabelPath = "/label";
    internal const string Document = "original";
    internal const string Before = "{\"label\":\"before\",\"value\":1}";
    internal const string After = "{\"label\":\"after\",\"value\":2}";
    internal const string BeforeSql = "SELECT * FROM _archive_entries WHERE label = 'before'";
    internal const string AfterSql = "SELECT * FROM _archive_entries WHERE label = 'after'";
    internal const string IndexAccess = "index:by-label";
    internal const string Put = "putDocument";
    internal const string ManifestUnsupported = "The backup manifest is unsupported.";
    internal const string IdentityUnsupported = "The database identity format is unsupported.";
    internal const string JournalInvalid = "A backup file failed verification.";
    internal const long InitialRevision = 1;
    internal const long HealthyRevision = 2;
    internal const long PositionStep = 1;
    internal const int Version = 1;
    internal const int ColdCycles = 2;
    internal const int NoFailures = 0;
    internal const int FailedExit = 1;
    internal static readonly string[] Files = [Manifest, Identity, Journal];
}
