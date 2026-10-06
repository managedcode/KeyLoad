using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using KeyLoad.Artifacts;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

internal static class CliBackupRestore
{
    private const int SourceArgumentIndex = 1;
    private const int DestinationArgumentIndex = 2;
    private const string MessagesBaseName = "KeyLoad.Cli.Features.BackupRestore.CliBackupRestoreMessages";
    private const string BackupVerifiedMessageKey = "BackupVerified";
    private const string ArchiveCreatedMessageKey = "ArchiveCreated";
    private const string ArchiveTransferFailedMessageKey = "ArchiveTransferFailed";
    private const string ArchiveCopiedMessageKey = "ArchiveCopied";
    private const string BackupExtractedMessageKey = "BackupExtracted";
    private static readonly ResourceManager Messages = new(
        MessagesBaseName,
        typeof(CliBackupRestore).Assembly);

    public static Task RunAsync(KeyLoadCliApplication.CliCommand command, string[] args,
        IOptions<ZoneTreeStorageExecutionOptions> storageOptions, IOptions<ZoneTreePointCacheExecutionOptions> cacheOptions,
        IOptions<CliBackupExecutionOptions> backupOptions)
    {
        switch (command)
        {
            case KeyLoadCliApplication.CliCommand.Backup:
                CreateBackup(args, storageOptions, cacheOptions);
                break;
            case KeyLoadCliApplication.CliCommand.Compact:
                Compact(args, storageOptions, cacheOptions);
                break;
            case KeyLoadCliApplication.CliCommand.Restore:
                Restore(args, storageOptions);
                break;
            case KeyLoadCliApplication.CliCommand.PackBackup:
                PackBackup(args, backupOptions);
                break;
            case KeyLoadCliApplication.CliCommand.InspectArtifact:
                InspectArtifact(args);
                break;
            case KeyLoadCliApplication.CliCommand.CopyArtifact:
                return CopyArtifactAsync(args);
            case KeyLoadCliApplication.CliCommand.UnpackBackup:
                UnpackBackup(args);
                break;
            default:
                throw new UnreachableException();
        }

        return Task.CompletedTask;
    }

    private static void CreateBackup(string[] args, IOptions<ZoneTreeStorageExecutionOptions> storageOptions,
        IOptions<ZoneTreePointCacheExecutionOptions> cacheOptions)
    {
        using (var store = new ZoneTreeStore(new(Path.GetFullPath(args[SourceArgumentIndex])), storageOptions, cacheOptions))
        {
            store.CreateBackup(Path.GetFullPath(args[DestinationArgumentIndex]));
        }

        Console.WriteLine(GetMessage(BackupVerifiedMessageKey));
    }

    private static void Compact(string[] args, IOptions<ZoneTreeStorageExecutionOptions> storageOptions,
        IOptions<ZoneTreePointCacheExecutionOptions> cacheOptions)
    {
        using var store = new ZoneTreeStore(new(Path.GetFullPath(args[SourceArgumentIndex])), storageOptions, cacheOptions);
        Console.WriteLine(JsonSerializer.Serialize(store.Compact(), JsonDefaults.Options));
    }

    private static void Restore(string[] args, IOptions<ZoneTreeStorageExecutionOptions> storageOptions)
    {
        var identity = ZoneTreeStore.Restore(Path.GetFullPath(args[SourceArgumentIndex]), Path.GetFullPath(args[DestinationArgumentIndex]), storageOptions);
        Console.WriteLine(JsonSerializer.Serialize(
            new { identity.NodeId, identity.Incarnation, identity.DispatchPaused },
            JsonDefaults.Options));
    }

    private static void PackBackup(string[] args, IOptions<CliBackupExecutionOptions> backupOptions)
    {
        BackupArtifact.Pack(args[SourceArgumentIndex], args[DestinationArgumentIndex], backupOptions.Value.PieceBytes);
        Console.WriteLine(GetMessage(ArchiveCreatedMessageKey));
    }

    private static void InspectArtifact(string[] args) => Console.WriteLine(JsonSerializer.Serialize(
        BackupArtifact.Inspect(args[SourceArgumentIndex]).Select(item => new { item.Name, item.Bytes, item.Pieces }),
        JsonDefaults.Options));

    private static async Task CopyArtifactAsync(string[] args)
    {
        var copied = await ArtifactTransfer.CopyToFileStorageAsync(args[SourceArgumentIndex], args[DestinationArgumentIndex]);
        if (copied.IsFailed)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, GetMessage(ArchiveTransferFailedMessageKey));
        }

        Console.WriteLine(GetMessage(ArchiveCopiedMessageKey));
    }

    private static void UnpackBackup(string[] args)
    {
        BackupArtifact.Unpack(args[SourceArgumentIndex], args[DestinationArgumentIndex]);
        Console.WriteLine(GetMessage(BackupExtractedMessageKey));
    }

    private static string GetMessage(string key) => Messages.GetString(key, CultureInfo.InvariantCulture)!;
}
