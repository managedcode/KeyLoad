using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EpochUpgradeFixture
{
    private const int FirstRecordFinalByte = 0xFF;
    private const int SecondRecordFinalByte = 0x22;

    private const long FirstRecordIndex = 0L;
    private const long SecondRecordIndex = 1L;
    private const int FirstRecordZeroByte = 0x00;
    private const int FirstRecordAsciiBoundary = 0x7F;
    private const int FirstRecordHighBit = 0x80;
    private const int SecondRecordFirstByte = 0x11;
    private const int SecondRecordZeroByte = 0x00;
    private const int SecondRecordPenultimateByte = 0xFE;
    private const int MarkerFirstByte = 0xE0;
    private const int MarkerZeroByte = 0x00;
    private const int MarkerLastByte = 0x0D;

    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";
    internal const string Namespace = "epoch-upgrade";
    internal const long AppliedPosition = 73;
    private const string MarkerKeyName = "marker";
    private const string RecordKeyName = "record";
    private const string SystemNamespace = "system";
    private const string LastAppliedKey = "last-applied";
    private const string OutcomePrincipal = "epoch-outcome-probe";
    private const string OutcomeAdminKey = "epoch-outcome-probe-owned-admin-key-2026";
    private const string OutcomeTenant = "epoch-outcome-tenant";
    private const string OutcomeDatabase = "epoch-outcome-database";
    private const string OutcomeResource = "epoch-outcome-resource";
    private const string OutcomeDomain = "epoch-outcome-domain";
    private const string OutcomeCreateFailure = "The prior outcome probe command did not commit successfully.";

    internal static byte[] FirstKey => KeyCodec.Encode(Namespace, RecordKeyName, FirstRecordIndex);
    internal static byte[] SecondKey => KeyCodec.Encode(Namespace, RecordKeyName, SecondRecordIndex);
    internal static byte[] MarkerKey => KeyCodec.Encode(Namespace, MarkerKeyName);
    internal static byte[] AppliedKey => KeyCodec.Encode(SystemNamespace, LastAppliedKey);
    internal static byte[] FirstValue => [FirstRecordZeroByte, FirstRecordAsciiBoundary, FirstRecordHighBit, FirstRecordFinalByte];
    internal static byte[] SecondValue => [SecondRecordFirstByte, SecondRecordZeroByte, SecondRecordPenultimateByte, SecondRecordFinalByte];
    internal static byte[] MarkerValue => [MarkerFirstByte, MarkerZeroByte, MarkerLastByte];
    internal const string OutcomePrincipalId = OutcomePrincipal;

    internal static byte[] CreateOutcomeFrame(ZoneTreeStore store, Guid commandId)
    {
        const long EmptyAppliedPosition = 0L;
        const string OutcomeNamespace = "outcome";

        ArgumentNullException.ThrowIfNull(store);
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AppliedKey, EmptyAppliedPosition);
            return true;
        });
        BootstrapOutcomeNode(database);
        var definition = new ResourceDefinition(OutcomeResource, ResourceKind.Collection, OutcomeDomain);
        var request = new ConfigureResourceRequest(OutcomeTenant, OutcomeDatabase, definition);
        var operation = database.CreateNativeOperation(OperationKind.ConfigureResource, commandId, OutcomePrincipal,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var result = database.Apply(operation);
        if (result.Error is not null)
        {
            throw Errors.Fail(result.Error.Value, OutcomeCreateFailure);
        }
        var bytes = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(OutcomeNamespace, OutcomePrincipal, commandId)))
            ?? throw Errors.Fail(ErrorCode.Corruption, OutcomeCreateFailure);
        return bytes;
    }

    internal static void Seed(ZoneTreeStore store, bool compacted)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.SetDispatchPaused(true);
        store.Commit((transaction, _) =>
        {
            transaction.Put(FirstKey, FirstValue);
            transaction.Put(SecondKey, SecondValue);
            transaction.Put(MarkerKey, MarkerValue);
            transaction.PutRecord(AppliedKey, AppliedPosition);
            return true;
        });
        if (compacted)
        {
            store.Compact();
        }
    }

    internal static async Task<EpochPriorSourceReply> CreateNodeAsync(string directory, EpochPriorNodeProfile profile)
    {
        const int SigningKeyBytes = 32;
        const int MinimumAdminKeyCharacters = 32;
        const string CreateNodeAsyncDetailText = "The component node profile is invalid.";
        const string NodeOwnerLockName = "node.owner.lock";
        const int InitialTerm = 1;
        const int FirstAppliedIndex = 1;
        const int FinalAppliedIndex = 3;

        var configuration = CrashExecutionOptions.Configuration(profile.LocalId, [.. profile.Voters], directory, profile.Incarnation);
        var signing = Convert.FromBase64String(profile.SigningKey);
        if (signing.Length != SigningKeyBytes || profile.AdminKey.Length < MinimumAdminKeyCharacters)
        { throw Errors.Fail(ErrorCode.Validation, CreateNodeAsyncDetailText); }
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        using var owner = new FileStream(Path.Combine(directory, NodeOwnerLockName), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(owner.Name, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        using var canonical = new ZoneTreeStore(new(Path.Combine(directory, DatabaseDirectory))
        { Incarnation = profile.Incarnation, SigningKey = signing }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        using var replica = new ZoneTreeStore(new(Path.Combine(directory, ReplicaDirectory))
        { Incarnation = profile.Incarnation, SigningKey = signing }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
        BootstrapNode(database, profile.AdminKey);
        using var log = new DurableReplicaLog(replica, configuration, canonicalDatabase: database);
        var snapshots = new ReplicaSnapshotStore(canonical, log, configuration, CrashExecutionOptions.Replica());
        await using var materializer = new ReplicaMaterializer(database, log, snapshots, CrashExecutionOptions.Replica());
        using var deadline = new CancellationTokenSource(CrashExecutionOptions.Child().Value.EpochApplyTimeout);
        log.SaveTermAndVote(InitialTerm, profile.LocalId);
        for (var index = FirstAppliedIndex; index <= FinalAppliedIndex; index++)
        {
            log.Append([new(index, InitialTerm, null)]);
            materializer.Commit(index);
            await materializer.WaitForApplyAsync(index, deadline.Token);
            _ = snapshots.Create(index, InitialTerm);
        }
        return EpochPriorSourceReply.Succeeded(canonical.Identity, canonical.Position, database.LastApplied);
    }

    private static void BootstrapOutcomeNode(DatabaseEngine database)
    {
        const string BootstrapOutcomeNodeTenantIdText = "system";
        const string BootstrapOutcomeNodeDatabaseText = "*";
        const string BootstrapOutcomeNodeResourceText = "*";
        const string BootstrapOutcomeNodeFieldGrantsText = "*";
        const string BootstrapOutcomeNodeIdText = "outcome-probe-key";

        database.Bootstrap(new(OutcomePrincipal, BootstrapOutcomeNodeTenantIdText, [new(BootstrapOutcomeNodeDatabaseText, BootstrapOutcomeNodeResourceText, Capability.All)], [BootstrapOutcomeNodeFieldGrantsText])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(BootstrapOutcomeNodeIdText, OutcomePrincipal, OutcomeAdminKey));
    }

    private static void BootstrapNode(DatabaseEngine database, string key)
    {
        const string BootstrapNodeIdText = "root";
        const string BootstrapNodeTenantIdText = "system";
        const string BootstrapNodeDatabaseText = "*";
        const string BootstrapNodeResourceText = "*";
        const string BootstrapNodeFieldGrantsText = "*";
        const string BootstrapNodePrincipalText = "root";

        database.Bootstrap(new(BootstrapNodeIdText, BootstrapNodeTenantIdText, [new(BootstrapNodeDatabaseText, BootstrapNodeResourceText, Capability.All)], [BootstrapNodeFieldGrantsText])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(BootstrapNodeIdText, BootstrapNodePrincipalText, key));
    }

    internal static (byte[] Key, byte[] Value)[] RawRecords() =>
    [
        (FirstKey, FirstValue),
        (SecondKey, SecondValue),
        (MarkerKey, MarkerValue)
    ];
}

internal sealed record EpochPriorNodeProfile(Guid Incarnation, string SigningKey, string AdminKey,
    string LocalId, string[] Voters);
