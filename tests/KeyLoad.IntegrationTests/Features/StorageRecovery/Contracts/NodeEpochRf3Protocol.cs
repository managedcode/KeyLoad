namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3Protocol
{
    internal const string Node1 = "node1";
    internal const string Node2 = "node2";
    internal const string Node3 = "node3";
    internal const string DataRootArgument = "--KeyLoad:DataRoot=";
    internal const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    internal const string SnapshotThresholdArgument = "--KeyLoad:SnapshotThreshold=16";
    internal const string ServerImageArgument = "--KeyLoad:ContainerImages:Server=";
    internal const string PriorDirectory = "prior";
    internal const string CurrentDirectory = "current";
    internal const string NegativeDirectory = "negative";
    internal const string ProfileFile = "local-profile.json";
    internal const string UnknownEntry = "foreign-unknown-entry.bin";
    internal const string StageSuffix = ".node-upgrade";
    internal const string AdminSnapshotTool = "keyload_admin_dashboard";
    internal const int NodeCount = 3;
    internal const int MinimumCommands = 20;
    internal const int SnapshotThreshold = 16;
    internal const int MaximumProfileBytes = 4_096;
    internal const int BufferBytes = 65_536;
    internal const int MaximumFiles = 100_000;
    internal const int MaximumDirectories = 10_000;
    internal const int MaximumDepth = 64;
    internal const int MaximumPathCharacters = 4_096;
    internal const int MaximumTotalPathCharacters = 4_194_304;
    internal const long MaximumInventoryBytes = 549_755_813_888;
    internal static readonly TimeSpan ParentDeadline = TimeSpan.FromMinutes(12);
    internal static readonly TimeSpan WaveDeadline = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(60);
    internal static readonly DateTimeOffset SampleStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
