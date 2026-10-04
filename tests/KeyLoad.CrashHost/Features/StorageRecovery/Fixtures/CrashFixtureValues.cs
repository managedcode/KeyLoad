namespace KeyLoad.CrashHost;

internal static class CrashFixtureValues
{
    internal const string CommitMode = "commit";
    internal const string CompactMode = "compact";
    internal const string InstallMode = "install";
    internal const string ProjectionMode = "projection-processing";
    internal const string SubscriptionMode = "subscription-processing";
    // Fixed child-process protocol tokens. They are machine signals, not localized console text.
    internal const string CrashMarker = "crash-point";
    internal const string Acknowledgement = "ack";
    internal const string ProcessingCommandFile = "processing-command.json";
    internal const string IncomingSnapshotFile = "incoming.snapshot";
    internal const string SnapshotSourceDirectory = "snapshot-source";
    internal const string Principal = "root";
    internal const string System = "system";
    internal const string Wildcard = "*";
    internal const string Credential = "root.crash-test-credential-32-characters";
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Orders = "orders";
    internal const string Partition = "partition";
    internal const string Projection = "projection";
    internal const string ProjectionConsumer = "projection-v1";
    internal const string ProjectionCommandId = "a64f0bcf-93ca-4c6e-a1ca-6f97e1ddbb66";
    internal const string SubscriptionCommandId = "cd8b971e-e8ee-4c83-bace-1c30954d097b";
    internal const string Topic = "topic";
    internal const string Group = "group";
    internal const string Input = "input";
    internal const string Effect = "effect";
    internal const string Handler = "handler";
    internal const string EventType = "Created";
    internal const string EmptyJson = "{}";
    internal const string EffectJson = "{\"complete\":true}";
    internal const string ItemKey = "item";
    internal const string ObsoleteKey = "obsolete";
    internal const string AppliedKey = "last-applied";
    internal const int ItemCount = 3;
    internal const int LeaseSeconds = 300;
    internal const long InitialCrashPosition = 2;
    internal const long SnapshotPreviousCut = 8;
    internal const long SnapshotCut = 9;
}
