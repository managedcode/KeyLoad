using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EpochUpgradeFixture
{
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

    internal static byte[] FirstKey => KeyCodec.Encode(Namespace, RecordKeyName, 0L);
    internal static byte[] SecondKey => KeyCodec.Encode(Namespace, RecordKeyName, 1L);
    internal static byte[] MarkerKey => KeyCodec.Encode(Namespace, MarkerKeyName);
    internal static byte[] AppliedKey => KeyCodec.Encode(SystemNamespace, LastAppliedKey);
    internal static byte[] FirstValue => [0x00, 0x7F, 0x80, 0xFF];
    internal static byte[] SecondValue => [0x11, 0x00, 0xFE, 0x22];
    internal static byte[] MarkerValue => [0xE0, 0x00, 0x0D];
    internal const string OutcomePrincipalId = OutcomePrincipal;

    internal static byte[] CreateOutcomeFrame(ZoneTreeStore store, Guid commandId)
    {
        ArgumentNullException.ThrowIfNull(store);
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource());
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AppliedKey, 0L);
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
        var bytes = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode("outcome", OutcomePrincipal, commandId)))
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
        var configuration = new ReplicaConfiguration(profile.LocalId, [.. profile.Voters], directory, profile.Incarnation);
        configuration.Validate();
        var signing = Convert.FromBase64String(profile.SigningKey);
        if (signing.Length != 32 || profile.AdminKey.Length < 32)
        { throw Errors.Fail(ErrorCode.Validation, "The component node profile is invalid."); }
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        using var owner = new FileStream(Path.Combine(directory, "node.owner.lock"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(owner.Name, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        using var canonical = new ZoneTreeStore(new(Path.Combine(directory, "database"))
        { Incarnation = profile.Incarnation, SigningKey = signing }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        using var replica = new ZoneTreeStore(new(Path.Combine(directory, "replica"))
        { Incarnation = profile.Incarnation, SigningKey = signing }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource());
        BootstrapNode(database, profile.AdminKey);
        using var log = new DurableReplicaLog(replica, CrashExecutionOptions.Configuration(configuration), canonicalDatabase: database);
        var snapshots = new ReplicaSnapshotStore(canonical, log, CrashExecutionOptions.Configuration(configuration), CrashExecutionOptions.Replica());
        await using var materializer = new ReplicaMaterializer(database, log, snapshots, CrashExecutionOptions.Replica());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        log.SaveTermAndVote(1, profile.LocalId);
        for (var index = 1; index <= 3; index++)
        {
            log.Append([new(index, 1, null)]);
            materializer.Commit(index);
            await materializer.WaitForApplyAsync(index, deadline.Token);
            _ = snapshots.Create(index, 1);
        }
        return EpochPriorSourceReply.Succeeded(canonical.Identity, canonical.Position, database.LastApplied);
    }

    private static void BootstrapOutcomeNode(DatabaseEngine database)
    {
        database.Bootstrap(new(OutcomePrincipal, "system", [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential("outcome-probe-key", OutcomePrincipal, OutcomeAdminKey));
    }

    private static void BootstrapNode(DatabaseEngine database, string key)
    {
        database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential("root", "root", key));
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
