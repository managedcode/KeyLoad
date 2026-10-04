namespace KeyLoad.Server;

internal static class ServerNodeUpgradeProtocol
{
    internal const string PreparedAlias = "keyload.server.node.upgrade.receipt.v1";
    internal const string OwnerAlias = "keyload.server.node.upgrade.owner.v1";
    internal const string EnvelopeAlias = "keyload.server.node.upgrade.envelope.v1";
    internal const string ProgressAlias = "keyload.server.node.upgrade.progress.v1";
    internal const string OwnerReceipt = "node-upgrade.owner.bin";
    internal const string ProgressReceipt = "node-upgrade.stage.bin";
    internal const string PreparedImages = "node-upgrade-images";
    internal const ulong ProgressMagic = 0x315453444C4BUL;
    internal const string PreparedReceipt = "node-upgrade.bin";
    internal const string Canonical = "database";
    internal const string Replica = "replica";
    internal const string Snapshots = "snapshots";
    internal const string Backups = "backups";
    internal const string SearchIndexes = "search-indexes";
    internal const string NodeOwner = "node.owner.lock";
    internal const string StoreOwner = "owner.lock";
    internal const string Identity = "identity.json";
    internal const string Journal = "commands.wal";
    internal const string Inputs = "node-upgrade-inputs";
    internal const string StageSuffix = ".node-upgrade";
    internal const ulong PreparedMagic = 0x31554E444C4BUL;
    internal const ulong OwnerMagic = 0x31574F444C4BUL;
    internal const int MaximumReceiptBytes = 1_048_576;
    internal const int MaximumEntries = 110_000;
    internal const int MaximumFiles = 100_000;
    internal const int MaximumDirectories = 10_000;
    internal const int MaximumDepth = 64;
    internal const int MaximumPathCharacters = 4_096;
    internal const int MaximumTotalPathCharacters = 4_194_304;
    internal const long MaximumSourceBytes = 549_755_813_888;
    internal const int BufferBytes = 65_536;
    internal const int OwnerFormatVersion = 2;
    internal const int ReceiptFormatVersion = 2;
    internal const int ProgressFormatVersion = 2;
    internal const int Native5SourceEpoch = 5;
    internal const int Native6SourceEpoch = 6;
    internal const int TargetEpoch = 7;
    internal const string Invalid = "The stopped node upgrade has an unsupported path, receipt or artifact.";
    internal const string Corrupt = "The stopped node upgrade source or target does not match its verified authority.";
    internal const string Limit = "The stopped node inventory exceeds its finite resource budget.";
    internal const string Pending = "Settle the pending snapshot transfer with the matching prior executable before upgrading.";
}
