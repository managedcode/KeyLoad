using System.Globalization;
using KeyLoad.Cli.Features.BackupRestore;

namespace KeyLoad.CrashHost;

/// <summary>Real original CLI coordinator paused only after its selected native publication barrier.</summary>
internal static class ClusterRestoreProcessCutScenario
{
    internal const string Mode = "native-cluster-restore-cut";
    internal const string MarkerPrefix = "KEYLOAD_RESTORE_STAGE";
    internal const string Separator = "|";
    private const string OperationFormat = "N";
    private const int NoArguments = 0;
    private const int ModeIndex = 0;
    private const int StageIndex = 1;
    private const int OperationIndex = 2;
    private const int ArgumentCount = 3;
    private const string Invalid = "The native restore process-cut fixture identity is invalid.";

    internal static Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == NoArguments || args[ModeIndex] != Mode)
        { return Task.FromResult(false); }
        if (args.Length != ArgumentCount || !Enum.TryParse<NativeClusterRestoreStage>(args[StageIndex], out var selected)
            || !Enum.IsDefined(selected) || selected == NativeClusterRestoreStage.None || !Guid.TryParseExact(args[OperationIndex], OperationFormat, out var operation)
            || operation == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var configuration = ClusterRestoreConfigurationBinding.Read();
        if (configuration.Value.OperationId != operation)
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        var original = ClusterRestoreCoordinator.Run(configuration, CliStorageConfiguration.Read().Storage,
            ClusterRestoreConfigurationBinding.ReadLimits(), TimeProvider.System, actual =>
            {
                if (actual != selected)
                { return; }
                Console.WriteLine(MarkerPrefix + Separator + actual + Separator + operation.ToString(OperationFormat)
                    + Separator + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                Console.Out.Flush();
                CrashHostPause.AtBoundary();
            }, CancellationToken.None);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(original)));
        return Task.FromResult(true);
    }
}
