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

    internal static byte[] FirstKey => KeyCodec.Encode(Namespace, RecordKeyName, 0L);
    internal static byte[] SecondKey => KeyCodec.Encode(Namespace, RecordKeyName, 1L);
    internal static byte[] MarkerKey => KeyCodec.Encode(Namespace, MarkerKeyName);
    internal static byte[] AppliedKey => KeyCodec.Encode(SystemNamespace, LastAppliedKey);
    internal static byte[] FirstValue => [0x00, 0x7F, 0x80, 0xFF];
    internal static byte[] SecondValue => [0x11, 0x00, 0xFE, 0x22];
    internal static byte[] MarkerValue => [0xE0, 0x00, 0x0D];

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
        { Incarnation = profile.Incarnation, SigningKey = signing });
        using var replica = new ZoneTreeStore(new(Path.Combine(directory, "replica"))
        { Incarnation = profile.Incarnation, SigningKey = signing });
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy());
        BootstrapNode(database, profile.AdminKey);
        using var log = new DurableReplicaLog(replica, configuration, canonicalDatabase: database);
        var snapshots = new ReplicaSnapshotStore(canonical, log, configuration);
        await using var materializer = new ReplicaMaterializer(database, log, snapshots);
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
