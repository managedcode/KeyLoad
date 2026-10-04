using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using KeyLoad.Artifacts;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Cli.Features.BackupRestore;

internal static class CliBackupRestore
{
    private const string MessagesBaseName = "KeyLoad.Cli.Features.BackupRestore.CliBackupRestoreMessages";
    private const string BackupVerifiedMessageKey = "BackupVerified";
    private const string ArchiveCreatedMessageKey = "ArchiveCreated";
    private const string ArchiveTransferFailedMessageKey = "ArchiveTransferFailed";
    private const string ArchiveCopiedMessageKey = "ArchiveCopied";
    private const string BackupExtractedMessageKey = "BackupExtracted";
    private static readonly ResourceManager Messages = new(
        MessagesBaseName,
        typeof(CliBackupRestore).Assembly);

    public static Task RunAsync(KeyLoadCliApplication.CliCommand command, string[] args)
    {
        switch (command)
        {
            case KeyLoadCliApplication.CliCommand.Backup:
                CreateBackup(args);
                break;
            case KeyLoadCliApplication.CliCommand.Compact:
                Compact(args);
                break;
            case KeyLoadCliApplication.CliCommand.Restore:
                Restore(args);
                break;
            case KeyLoadCliApplication.CliCommand.PackBackup:
                PackBackup(args);
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

    private static void CreateBackup(string[] args)
    {
        using (var store = new ZoneTreeStore(new(Path.GetFullPath(args[1]))))
        {
            store.CreateBackup(Path.GetFullPath(args[2]));
        }

        Console.WriteLine(GetMessage(BackupVerifiedMessageKey));
    }

    private static void Compact(string[] args)
    {
        using var store = new ZoneTreeStore(new(Path.GetFullPath(args[1])));
        Console.WriteLine(JsonSerializer.Serialize(store.Compact(), JsonDefaults.Options));
    }

    private static void Restore(string[] args)
    {
        var identity = ZoneTreeStore.Restore(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        Console.WriteLine(JsonSerializer.Serialize(
            new { identity.NodeId, identity.Incarnation, identity.DispatchPaused },
            JsonDefaults.Options));
    }

    private static void PackBackup(string[] args)
    {
        BackupArtifact.Pack(args[1], args[2]);
        Console.WriteLine(GetMessage(ArchiveCreatedMessageKey));
    }

    private static void InspectArtifact(string[] args) => Console.WriteLine(JsonSerializer.Serialize(
        BackupArtifact.Inspect(args[1]).Select(item => new { item.Name, item.Bytes, item.Pieces }),
        JsonDefaults.Options));

    private static async Task CopyArtifactAsync(string[] args)
    {
        var copied = await ArtifactTransfer.CopyToFileStorageAsync(args[1], args[2]);
        if (copied.IsFailed)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, GetMessage(ArchiveTransferFailedMessageKey));
        }

        Console.WriteLine(GetMessage(ArchiveCopiedMessageKey));
    }

    private static void UnpackBackup(string[] args)
    {
        BackupArtifact.Unpack(args[1], args[2]);
        Console.WriteLine(GetMessage(BackupExtractedMessageKey));
    }

    private static string GetMessage(string key) => Messages.GetString(key, CultureInfo.InvariantCulture)!;
}
