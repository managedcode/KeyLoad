using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RuntimeJournalReopenFixture : IDisposable
{
    private const string DirectoryPrefix = "keyload-runtime-journal-reopen-";
    private const string RootId = "runtime-journal-root";
    private const string RootKeyId = "runtime-journal-root-key";
    private const string RootSecret = "runtime-journal-reopen.unit-test-credential-32-characters";
    private const string Wildcard = "*";
    private readonly string directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
    private ZoneTreeStore? store;
    private readonly RuntimeJournalOptions options = new();

    internal DatabaseEngine Database { get; private set; } = null!;
    internal int MinimumReaderContract => store?.Identity.MinimumReaderContract
        ?? throw new ObjectDisposedException(nameof(RuntimeJournalReopenFixture));

    internal RuntimeJournalReopenFixture()
    {
        Open();
        Database.Bootstrap(new PrincipalRecord(RootId, "system", [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootKeyId, RootId, RootSecret));
        ClusterPrincipalPolicy.Initialize(Database);
        Database.ConfigureRuntimeJournal(Options.Create(options));
        Submit(new(RuntimeJournalAction.BootstrapIdentity, string.Empty, Guid.Empty, 0, 0, null,
            ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []), RootId);
    }

    internal RuntimeJournalMutationResult Submit(RuntimeJournalMutation mutation, string principal = "keyload-internal-runtime-journal")
        => Database.Apply(new(Guid.NewGuid(), OperationKind.RuntimeJournal, principal, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(mutation, JsonDefaults.Options))).Get<RuntimeJournalMutationResult>();

    internal void Reopen()
    {
        store?.Dispose();
        Open();
        ClusterPrincipalPolicy.Initialize(Database);
        Database.ConfigureRuntimeJournal(Options.Create(options));
    }

    public void Dispose()
    {
        store?.Dispose();
        store = null;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private void Open()
    {
        store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        Database = new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
    }
}
