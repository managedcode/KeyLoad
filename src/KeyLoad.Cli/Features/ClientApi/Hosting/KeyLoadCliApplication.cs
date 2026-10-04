using System.Diagnostics;
using System.Text.Json;
using KeyLoad;
using KeyLoad.Cli.Features.BackupRestore;
using KeyLoad.Cli.Features.ClientApi;

internal static class KeyLoadCliApplication
{
    private const string StatusCommand = "status";
    private const string BackupCommand = "backup";
    private const string CompactCommand = "compact";
    private const string RestoreCommand = "restore";
    private const string PackBackupCommand = "pack-backup";
    private const string InspectArtifactCommand = "inspect-artifact";
    private const string CopyArtifactCommand = "copy-artifact";
    private const string UnpackBackupCommand = "unpack-backup";

    public static async Task RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                CliClientApi.Help();
                return;
            }

            if (!TryGetCommand(args, out var command))
            {
                CliClientApi.Help();
                Environment.ExitCode = 2;
                return;
            }

            await DispatchAsync(command, args);
        }
        catch (KeyLoadException exception)
        {
            var problem = JsonSerializer.Serialize(exception.ToProblem(), JsonDefaults.Options);
            await Console.Error.WriteLineAsync(problem);
            Environment.ExitCode = 1;
        }
    }

    private static bool TryGetCommand(string[] args, out CliCommand command)
    {
        command = args[0] switch
        {
            StatusCommand when args.Length is 2 or 3 => CliCommand.Status,
            BackupCommand when args.Length == 3 => CliCommand.Backup,
            CompactCommand when args.Length == 2 => CliCommand.Compact,
            RestoreCommand when args.Length == 3 => CliCommand.Restore,
            PackBackupCommand when args.Length == 3 => CliCommand.PackBackup,
            InspectArtifactCommand when args.Length == 2 => CliCommand.InspectArtifact,
            CopyArtifactCommand when args.Length == 3 => CliCommand.CopyArtifact,
            UnpackBackupCommand when args.Length == 3 => CliCommand.UnpackBackup,
            _ => CliCommand.None
        };

        return command is not CliCommand.None;
    }

    private static Task DispatchAsync(CliCommand command, string[] args) => command switch
    {
        CliCommand.Status => CliClientApi.StatusAsync(args),
        CliCommand.Backup or CliCommand.Compact or CliCommand.Restore or
        CliCommand.PackBackup or CliCommand.InspectArtifact or CliCommand.CopyArtifact or
        CliCommand.UnpackBackup => CliBackupRestore.RunAsync(command, args),
        _ => throw new UnreachableException()
    };

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
