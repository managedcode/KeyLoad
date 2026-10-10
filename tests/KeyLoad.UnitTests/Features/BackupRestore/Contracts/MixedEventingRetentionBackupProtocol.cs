namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupProtocol
{
    internal const string Topic = "mixed-purged-topic";
    internal const string Group = "mixed-purge-pin";
    internal const string EventType = "MixedRetention";
    internal const string Payload = "{\"retained\":\"original mixed cut\"}";
    internal const string Headers = "{\"lineage\":\"native backup\"}";
    internal const string First = "mixed-one";
    internal const string Second = "mixed-two";
    internal const string Third = "mixed-three";
    internal const string Backup = "mixed-backup";
    internal const string Archive = "mixed-backup.ctg";
    internal const string Unpacked = "mixed-unpacked";
    internal const string Target = "mixed-target";
    internal const int PieceBytes = 1_024;
    internal const long PurgePosition = 2;
    internal const long LastPosition = 3;
    internal const long FirstSequence = 4;
    internal const long LastSequence = 6;
    internal const long HealthySequence = 8;
}
