using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class BackupIndexedDocumentDedupFixture : IDisposable
{
    private const string DirectoryPrefix = "keyload-backup-indexed-dedup-";
    private const string GuidFormat = "N";
    private const string RootPrincipal = "root";
    private const string MembershipContents = "{}";
    private const long InitialAppliedIndex = 1;
    private const string SystemNamespace = ZoneTreePersistenceFormat.SystemNamespace;
    private const string MembershipNamespace = ZoneTreePersistenceFormat.MembershipNamespace;
    private const string LastAppliedKey = ZoneTreePersistenceFormat.LastAppliedKey;
    private const string ClockKey = ZoneTreePersistenceFormat.ClockKey;
    private readonly string root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private ZoneTreeStore? restoredStore;

    internal const string Collection = "backup_documents";
    internal const string IndexName = "by-label";
    internal const string LabelPath = "/label";
    internal const string FirstId = "doc-a";
    internal const string SecondId = "doc-b";
    internal const string DeletedId = "doc-c";
    internal const string RestoredId = "doc-d";
    internal const string HealthyId = "doc-e";
    internal const string TransientId = "transient-conflict";
    internal const string AlphaLabel = "alpha";
    internal const string CobaltLabel = "cobalt";
    internal const string JadeLabel = "jade";
    internal const string VioletLabel = "violet";
    internal const string AmberLabel = "amber";
    internal const string GoldLabel = "gold";
    internal const string ReplacedLabelJson = "\"violet\"";
    internal const string TemporaryConflictJson = "{\"label\":\"temporary\",\"rank\":9}";
    internal const string FirstJson = "{\"label\":\"alpha\",\"rank\":1}";
    internal const string SecondJson = "{\"label\":\"cobalt\",\"rank\":2}";
    internal const string DeletedJson = "{\"label\":\"jade\",\"rank\":3}";
    internal const string ReplacedJson = "{\"label\":\"violet\",\"rank\":1}";
    internal const string RestoredJson = "{\"label\":\"amber\",\"rank\":4}";
    internal const string HealthyJson = "{\"label\":\"gold\",\"rank\":5}";
    internal const string MembershipKey = ZoneTreePersistenceFormat.OrleansMembershipKey;
    internal const long FirstRevision = 1;
    internal const long SecondRevision = 2;
    internal const long NoPriorRevision = 0;
    internal static readonly Guid MembershipCommandId = Guid.Parse("c21ccfc6-6aac-4539-bd0f-c2d4e4fd9c1c");

    internal string BackupDirectory { get; }
    internal string TargetDirectory { get; }
    internal TestDatabase Source { get; }
    internal DatabaseEngine RestoredDatabase { get; private set; } = null!;
    internal ZoneTreeStore TargetStore => restoredStore ?? throw new InvalidOperationException("The restored store is closed.");
    internal PartitionRef Partition => Source.Partition;

    internal BackupIndexedDocumentDedupFixture()
    {
        Directory.CreateDirectory(root);
        var sourceDirectory = Path.Combine(root, "source");
        BackupDirectory = Path.Combine(root, "backup");
        TargetDirectory = Path.Combine(root, "restored");
        Source = new(directory: sourceDirectory);
    }

    internal ReplicatedOperation Batch(DatabaseEngine database, Guid commandId, params Mutation[] mutations)
        => new(commandId, OperationKind.Batch, RootPrincipal, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new CommandRequest(commandId, Partition, [.. mutations]), JsonDefaults.Options));

    internal OperationResult SeedReplicaAuthority()
    {
        ClusterPrincipalPolicy.Initialize(Source.Database);
        var membership = new MembershipMutation(MembershipKey, NoPriorRevision,
            NativeSerialization.Serialize(MembershipContents));
        var operation = new ReplicatedOperation(MembershipCommandId,
            OperationKind.Membership, ClusterPrincipalPolicy.InternalPrincipalId,
            Source.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(membership, JsonDefaults.Options));
        return Source.Database.Apply(operation, InitialAppliedIndex);
    }

    internal async Task<Dictionary<string, byte[]>> CaptureArchiveAsync()
    {
        var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*", SearchOption.AllDirectories))
        {
            captured.Add(Path.GetRelativePath(BackupDirectory, file), await File.ReadAllBytesAsync(file));
        }
        return captured;
    }

    internal static void RequireSuccess(OperationResult result)
    {
        if (result.Error is not null)
        {
            throw new InvalidOperationException("A canonical backup fixture operation was rejected.");
        }
    }

    internal void AssertSourceAuthority()
    {
        var applied = Source.Store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(SystemNamespace, LastAppliedKey)));
        var clock = Source.Store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(SystemNamespace, ClockKey)));
        var membership = Source.Store.Read(view => view.GetRecord<MembershipRecord>(KeyCodec.Encode(
            MembershipNamespace, MembershipKey)));
        if (applied is null || NativeSerialization.Deserialize<long>(applied) != InitialAppliedIndex
            || clock is null || membership is null || membership.Version != FirstRevision
            || NativeSerialization.Deserialize<string>(membership.Payload.Span) != MembershipContents)
        {
            throw new InvalidOperationException("The source backup did not contain the exercised replica authority fields.");
        }
    }

    internal long CreateBackup() => Source.Store.CreateBackup(BackupDirectory);

    internal StoreIdentity Restore() => ZoneTreeStore.Restore(BackupDirectory, TargetDirectory,
        UnitExecutionOptions.StorageExecution());

    internal DatabaseEngine OpenRestored()
    {
        restoredStore = new(new ZoneTreeStoreOptions(TargetDirectory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        RestoredDatabase = CreateDatabase(restoredStore);
        return RestoredDatabase;
    }

    internal void CloseRestored()
    {
        restoredStore?.Dispose();
        restoredStore = null;
        RestoredDatabase = null!;
    }

    internal static DatabaseEngine CreateDatabase(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), TimeProvider.System);

    internal static byte[] ReadOutcomeBytes(IAtomicStore store, PartitionRef partition, string principal, Guid commandId)
        => store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(partition, principal, commandId)))
            ?? throw new InvalidOperationException("The expected command outcome is absent.");

    public void Dispose()
    {
        try
        {
            CloseRestored();
        }
        finally
        {
            try
            {
                Source.Dispose();
            }
            finally
            {
                if (Directory.Exists(root))
                { Directory.Delete(root, recursive: true); }
            }
        }
    }
}
