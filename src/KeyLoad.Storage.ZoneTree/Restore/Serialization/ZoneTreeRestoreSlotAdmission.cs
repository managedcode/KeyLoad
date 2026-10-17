namespace KeyLoad.Storage.ZoneTree;

[Orleans.GenerateSerializer, Orleans.Alias(ZoneTreeRestoreSlotAdmission.SerializerAlias)]
internal sealed record ZoneTreeRestoreSlotAdmission(
    [property: Orleans.Id(ZoneTreeRestoreSlotAdmission.VersionField)] int Version,
    [property: Orleans.Id(ZoneTreeRestoreSlotAdmission.ContextField)] ClusterRestoreSlotContext Context,
    [property: Orleans.Id(ZoneTreeRestoreSlotAdmission.FilesField)] ZoneTreeBackupRestoreManifestFile[] SourceFiles,
    [property: Orleans.Id(ZoneTreeRestoreSlotAdmission.FirstField)] DateTimeOffset FirstAdmittedAt)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore.slot-admission.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int ContextField = 1;
    private const int FilesField = 2;
    private const int FirstField = 3;
}
