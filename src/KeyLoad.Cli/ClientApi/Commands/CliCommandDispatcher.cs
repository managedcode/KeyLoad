using System.Diagnostics;
using KeyLoad.Cli.BackupRestore;
using KeyLoad.Cli.ClientApi;

internal static class CliCommandDispatcher
{
    private const int OneOperandArguments = 2;
    private const int TwoOperandArguments = 3;
    private const string StatusCommand = "status";
    private const string BackupCommand = "backup";
    private const string RestoreCommand = "restore";
    private const string CopyBackupCommand = "copy-backup";

    internal static bool TryGetCommand(string[] args, out CliCommand command)
    {
        command = args[0] switch
        {
            StatusCommand when args.Length == OneOperandArguments => CliCommand.Status,
            BackupCommand when args.Length == TwoOperandArguments => CliCommand.Backup,
            RestoreCommand when args.Length == TwoOperandArguments => CliCommand.Restore,
            CopyBackupCommand when args.Length == TwoOperandArguments => CliCommand.CopyBackup,
            _ => CliCommand.None
        };

        return command is not CliCommand.None;
    }

    internal static Task DispatchAsync(CliCommand command, string[] args) => command switch
    {
        CliCommand.Status => RunStatusAsync(args),
        CliCommand.Backup or CliCommand.Restore or CliCommand.CopyBackup => RunBackupRestoreAsync(command, args),
        _ => throw new UnreachableException()
    };

    private static Task RunStatusAsync(string[] args)
    {
        var options = CliClientConfiguration.Read(args);
        return CliClientApi.StatusAsync(options.Connection, options.Execution, options.ClientExecution);
    }

    private static Task RunBackupRestoreAsync(CliCommand command, string[] args)
    {
        var options = CliStorageConfiguration.Read();
        return CliBackupRestore.RunAsync(command, args, options.Storage);
    }

    internal enum CliCommand
    {
        None,
        Status,
        Backup,
        Restore,
        CopyBackup
    }
}
