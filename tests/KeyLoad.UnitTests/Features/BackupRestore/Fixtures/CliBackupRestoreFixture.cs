using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreFixture
{
    internal const string StoredKey = "cli-backup/seed";
    internal const string StoredValue = "canonical-cli-backup-value";
    internal const string BackupDirectoryName = "backup";
    internal const string ArtifactFileName = "backup.ctg";
    internal const string CopiedArtifactDirectoryName = "copied-artifact";
    internal const string UnpackedDirectoryName = "unpacked";
    internal const string RestoredDirectoryName = "restored";
    internal const string ExistingDestinationName = "existing-destination";
    internal const string PreservedFileName = "preserved.txt";
    internal const string PreservedFileContents = "preserve-existing-destination";

    private const string RootPrefix = "keyload-cli-backup-flow-";
    private const string MissingIdentityMessage = "The CLI backup fixture has not initialized its real store identity.";
    private StoreIdentity? InitializedIdentity { get; set; }
    private bool rootOwned;
    private bool storeClosed;

    internal string Root { get; } = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
    internal string SourceDirectory => Path.Combine(Root, "source");
    internal string BackupDirectory => Path.Combine(Root, BackupDirectoryName);
    internal string ArtifactPath => Path.Combine(Root, ArtifactFileName);
    internal string CopiedArtifactDirectory => Path.Combine(Root, CopiedArtifactDirectoryName);
    internal string CopiedArtifactPath => Path.Combine(CopiedArtifactDirectory, ArtifactFileName);
    internal string UnpackedDirectory => Path.Combine(Root, UnpackedDirectoryName);
    internal string RestoredDirectory => Path.Combine(Root, RestoredDirectoryName);
    internal string ExistingDestination => Path.Combine(Root, ExistingDestinationName);
    internal StoreIdentity OriginalIdentity => InitializedIdentity ?? throw new InvalidOperationException(MissingIdentityMessage);
    internal static byte[] StoredKeyBytes => KeyCodec.Encode(StoredKey);

    private CliBackupRestoreFixture() { }

    internal static async Task<string> RunAsync(Func<CliBackupRestoreFixture, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var fixture = new CliBackupRestoreFixture();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(fixture.Initialize, failures);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(() => operation(fixture), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(fixture.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        cancellationToken.ThrowIfCancellationRequested();
        return fixture.Root;
    }

    internal async Task AssertSeedRemainsAsync()
    {
        using var store = new ZoneTreeStore(new(SourceDirectory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var value = store.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(StoredKeyBytes)!));
        await Assert.That(value).IsEqualTo(StoredValue);
    }

    private void Initialize()
    {
        if (Directory.Exists(Root) || File.Exists(Root))
        {
            throw new IOException("The unique CLI backup fixture root already exists.");
        }
        Directory.CreateDirectory(Root);
        rootOwned = true;
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => store = new ZoneTreeStore(new(SourceDirectory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            InitializedIdentity = store.Identity;
            ServerFailureObserver.Observe(() => store.Commit((transaction, _) =>
            {
                transaction.PutRecord(StoredKeyBytes, StoredValue);
                return true;
            }), failures);
            var closeFailures = new List<Exception>();
            ServerFailureObserver.Observe(store.Dispose, closeFailures);
            storeClosed = closeFailures.Count == 0;
            failures.AddRange(closeFailures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Dispose()
    {
        if (rootOwned && storeClosed && Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
