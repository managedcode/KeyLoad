using System.Diagnostics;
using KeyLoad.Cli.Features.BackupRestore;
using KeyLoad.Cli.Features.ClientApi;

internal static class CliCommandDispatcher
{
    private const int CommandArgumentIndex = 0;
    private const int OneOperandArguments = 2;
    private const int TwoOperandArguments = 3;
    private const string StatusCommand = "status";
    private const string BackupCommand = "backup";
    private const string CompactCommand = "compact";
    private const string RestoreCommand = "restore";
    private const string PackBackupCommand = "pack-backup";
    private const string InspectArtifactCommand = "inspect-artifact";
    private const string CopyArtifactCommand = "copy-artifact";
    private const string UnpackBackupCommand = "unpack-backup";

    internal static bool TryGetCommand(string[] args, out CliCommand command)
    {
        command = args[CommandArgumentIndex] switch
        {
            StatusCommand when args.Length is OneOperandArguments or TwoOperandArguments => CliCommand.Status,
            BackupCommand when args.Length == TwoOperandArguments => CliCommand.Backup,
            CompactCommand when args.Length == OneOperandArguments => CliCommand.Compact,
            RestoreCommand when args.Length == TwoOperandArguments => CliCommand.Restore,
            PackBackupCommand when args.Length == TwoOperandArguments => CliCommand.PackBackup,
            InspectArtifactCommand when args.Length == OneOperandArguments => CliCommand.InspectArtifact,
            CopyArtifactCommand when args.Length == TwoOperandArguments => CliCommand.CopyArtifact,
            UnpackBackupCommand when args.Length == TwoOperandArguments => CliCommand.UnpackBackup,
            _ => CliCommand.None
        };

        return command is not CliCommand.None;
    }

    internal static Task DispatchAsync(CliCommand command, string[] args) => command switch
    {
        CliCommand.Status => RunStatusAsync(args),
        CliCommand.Backup or CliCommand.Compact or CliCommand.Restore or
        CliCommand.PackBackup or CliCommand.InspectArtifact or CliCommand.CopyArtifact or
        CliCommand.UnpackBackup => RunBackupRestoreAsync(command, args),
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
        return CliBackupRestore.RunAsync(command, args, options.Storage, options.PointCache, options.Backup);
    }

    internal enum CliCommand
    {
        None,
        Status,
        Backup,
        Compact,
        Restore,
        PackBackup,
        InspectArtifact,
        CopyArtifact,
        UnpackBackup
    }
}
