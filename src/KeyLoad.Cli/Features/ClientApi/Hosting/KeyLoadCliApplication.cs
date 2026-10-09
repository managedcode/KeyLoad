using System.Diagnostics;
using System.Text.Json;
using KeyLoad;
using KeyLoad.Cli.Features.BackupRestore;
using KeyLoad.Cli.Features.ClientApi;

internal static class KeyLoadCliApplication
{
    private const int NoArguments = 0;
    private const int CommandArgumentIndex = 0;
    private const int NoOperandArguments = 1;
    private const int OneOperandArguments = 2;
    private const int TwoOperandArguments = 3;
    private const int InvalidCommandExitCode = 2;
    private const int FailedOperationExitCode = 1;
    private const string StatusCommand = "status";
    private const string BackupCommand = "backup";
    private const string CompactCommand = "compact";
    private const string RestoreCommand = "restore";
    private const string RestoreClusterCommand = "restore-cluster";
    private const string PackBackupCommand = "pack-backup";
    private const string InspectArtifactCommand = "inspect-artifact";
    private const string CopyArtifactCommand = "copy-artifact";
    private const string UnpackBackupCommand = "unpack-backup";

    public static async Task RunAsync(string[] args)
    {
        try
        {
            _ = SerializationExecutionRegistration.Process.Value;
            if (args.Length == NoArguments)
            {
                CliClientApi.Help();
                return;
            }

            if (!TryGetCommand(args, out var command))
            {
                CliClientApi.Help();
                Environment.ExitCode = InvalidCommandExitCode;
                return;
            }

            await DispatchAsync(command, args);
        }
        catch (KeyLoadException exception)
        {
            var problem = JsonSerializer.Serialize(exception.ToProblem(), JsonDefaults.Options);
            await Console.Error.WriteLineAsync(problem);
            Environment.ExitCode = FailedOperationExitCode;
        }
    }

    private static bool TryGetCommand(string[] args, out CliCommand command)
    {
        command = args[CommandArgumentIndex] switch
        {
            StatusCommand when args.Length is OneOperandArguments or TwoOperandArguments => CliCommand.Status,
            BackupCommand when args.Length == TwoOperandArguments => CliCommand.Backup,
            CompactCommand when args.Length == OneOperandArguments => CliCommand.Compact,
            RestoreClusterCommand when args.Length == NoOperandArguments => CliCommand.RestoreCluster,
            RestoreCommand when args.Length == TwoOperandArguments => CliCommand.Restore,
            PackBackupCommand when args.Length == TwoOperandArguments => CliCommand.PackBackup,
            InspectArtifactCommand when args.Length == OneOperandArguments => CliCommand.InspectArtifact,
            CopyArtifactCommand when args.Length == TwoOperandArguments => CliCommand.CopyArtifact,
            UnpackBackupCommand when args.Length == TwoOperandArguments => CliCommand.UnpackBackup,
            _ => CliCommand.None
        };

        return command is not CliCommand.None;
    }

    private static Task DispatchAsync(CliCommand command, string[] args) => command switch
    {
        CliCommand.Status => RunStatusAsync(args),
        CliCommand.RestoreCluster => CliClusterRestore.RunAsync(),
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
        RestoreCluster,
        PackBackup,
        InspectArtifact,
        CopyArtifact,
        UnpackBackup
    }
}
