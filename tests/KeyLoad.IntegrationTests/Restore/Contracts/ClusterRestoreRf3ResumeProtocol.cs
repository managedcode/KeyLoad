namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3ResumeProtocol
{
    internal const string CrashHostDll = "tests/KeyLoad.CrashHost/bin/Release/net10.0/KeyLoad.CrashHost.dll";
    internal const string CutMode = "native-cluster-restore-cut";
    internal const string Marker = "KEYLOAD_RESTORE_STAGE";
    internal const char Separator = '|';
    internal const int MarkerFields = 4;
    internal const int StageField = 1;
    internal const int OperationField = 2;
    internal const int ProcessField = 3;
    internal const int MinimumPid = 1;
    internal const int OverflowByte = 1;
    internal const string ProcRoot = "/proc";
    internal const string CommandLine = "cmdline";
    internal const string Runtime = "dotnet";
    internal const string StateCorruption = "The native cluster restore operation state is corrupt or foreign.";
    internal const string PlanMismatch = "The supplied restore inputs differ from the original native operation plan.";
    internal const string TerminalCutLost = "The published restore target no longer has its original terminal native cut.";
    internal const char ArgumentSeparator = '\0';
    internal const string OperationPrefix = ".keyload-restore-";
    internal const string OperationOwnerSuffix = ".keyload-restore-owner";
    internal const string NodesDirectory = "nodes";
    internal const string PlanFile = "plan.native";
    internal const string ProgressFile = "progress.native";
    internal const string MissingProgress = "The original native restore progress authority is missing.";
    internal const string MissingSlot = "The original native restore slot differs from its admitted operation.";
    internal const string SlotSuffix = ".slot-stage";
    internal const string DatabaseOwner = "owner.lock";
    internal const string SlotOwner = "restore-slot.owner.lock";
    internal const int ProcessArguments = 5;
    internal const int ExecutableArgument = 0;
    internal const int AssemblyArgument = 1;
    internal const int ModeArgument = 2;
    internal const int StageArgument = 3;
    internal const int OperationArgument = 4;
}
