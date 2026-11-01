using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.BackupRestore;

internal static class CliBackupRestore
{
    private const int DestinationArgumentIndex = 2;
    private const string MessagesBaseName = "KeyLoad.Cli.Restore.CliBackupRestoreMessages";
    private const string BackupCreatedMessageKey = "BackupCreated";
    private const string BackupCopiedMessageKey = "BackupCopied";
    private const string BackupSourceUnavailableDetail = "The offline backup source could not be opened.";
    private static readonly ResourceManager Messages = new(
        MessagesBaseName,
        typeof(CliBackupRestore).Assembly);

    public static Task RunAsync(CliCommandDispatcher.CliCommand command, string[] args,
        IOptions<ZoneTreeStorageExecutionOptions> storageOptions)
        => command switch
        {
            CliCommandDispatcher.CliCommand.Backup => CreateBackupAsync(args, storageOptions),
            CliCommandDispatcher.CliCommand.Restore => RestoreAsync(args, storageOptions),
            CliCommandDispatcher.CliCommand.CopyBackup => CopyBackupAsync(args),
            _ => throw new UnreachableException()
        };

    private static async Task CreateBackupAsync(string[] args, IOptions<ZoneTreeStorageExecutionOptions> storageOptions)
    {
        using (var store = OpenBackupSource(Path.GetFullPath(args[1]), storageOptions))
        {
            await store.CreateBackupAsync(Path.GetFullPath(args[DestinationArgumentIndex]));
        }

        Console.WriteLine(GetMessage(BackupCreatedMessageKey));
    }

    private static ZoneTreeStore OpenBackupSource(string source,
        IOptions<ZoneTreeStorageExecutionOptions> storageOptions)
    {
        try
        {
            return new ZoneTreeStore(new(source), storageOptions);
        }
        catch (IOException original)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, BackupSourceUnavailableDetail, original);
        }
    }

    private static async Task RestoreAsync(string[] args, IOptions<ZoneTreeStorageExecutionOptions> storageOptions)
    {
        var identity = await ZoneTreeStore.RestoreAsync(Path.GetFullPath(args[1]),
            Path.GetFullPath(args[DestinationArgumentIndex]), storageOptions);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                identity.NodeId,
                identity.Incarnation,
                identity.DispatchPaused
            },
            JsonDefaults.Options));
    }

    private static async Task CopyBackupAsync(string[] args)
    {
        await ZoneTreeBackupCopy.CopyToFileStorageAsync(args[1], args[DestinationArgumentIndex]);
        Console.WriteLine(GetMessage(BackupCopiedMessageKey));
    }

    private static string GetMessage(string key) => Messages.GetString(key, CultureInfo.InvariantCulture)!;
}
