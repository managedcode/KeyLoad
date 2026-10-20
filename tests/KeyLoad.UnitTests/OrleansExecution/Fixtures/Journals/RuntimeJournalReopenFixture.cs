using System.Runtime.ExceptionServices;
using System.Text.Json;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

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

    private bool runtimeAcquisitionPending;
    private bool runtimeCloseAttempted;
    private ExceptionDispatchInfo? originalAcquisitionFailure;
    private ExceptionDispatchInfo? originalCloseFailure;
    private const string AcquisitionNotJoined = "The execution runtime journal owner acquisition is unknown; retain its root.";
    private const string CloseNotJoined = "The original execution runtime journal owner close is unknown; retain its root.";

    internal DatabaseEngine Database { get; private set; } = null!;
    internal int MinimumReaderContract => store?.Identity.MinimumReaderContract
        ?? throw new ObjectDisposedException(nameof(RuntimeJournalReopenFixture));

    internal RuntimeJournalReopenFixture()
    {
        var failures = new List<Exception>();
        KeyLoad.Server.ServerFailureObserver.Observe(Initialize, failures);
        if (failures.Count == 0)
        {
            return;
        }
        Cleanup(failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Initialize()
    {
        Open();
        Database.Bootstrap(new PrincipalRecord(RootId, "system", [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        {
            ClusterAdministrator = true
        }, AuthorizationExecution.Credential(RootKeyId, RootId, RootSecret));
        Database.ConfigureRuntimeJournal(Options.Create(options));
        Submit(new(RuntimeJournalAction.BootstrapIdentity, string.Empty, Guid.Empty, 0, 0, null,
            ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []), RootId);
    }

    internal RuntimeJournalMutationResult Submit(RuntimeJournalMutation mutation, string principal = "keyload-internal-runtime-journal")
        => Database.Apply(Database.CreateCommand(OperationKind.RuntimeJournal, Guid.NewGuid(), principal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(mutation, JsonDefaults.Options))).Get<RuntimeJournalMutationResult>();

    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(store is null && !runtimeAcquisitionPending, this);
        CloseOriginalStore();
        var failures = new List<Exception>();
        KeyLoad.Server.ServerFailureObserver.Observe(ReopenCore, failures);
        if (failures.Count == 0)
        {
            return;
        }
        Cleanup(failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ReopenCore()
    {
        Open();
        Database.ConfigureRuntimeJournal(Options.Create(options));
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        Cleanup(failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Open()
    {
        var storageOptions = new ZoneTreeStoreOptions(directory);
        var execution = UnitExecutionOptions.StorageExecution();
        var cache = UnitExecutionOptions.PointCacheExecution();
        runtimeAcquisitionPending = true;
        ZoneTreeStore opened;
        try
        {
            opened = new ZoneTreeStore(storageOptions, execution, cache);
            store = opened;
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        runtimeAcquisitionPending = false;
        runtimeCloseAttempted = false;
        originalAcquisitionFailure = null;
        originalCloseFailure = null;
        Database = new(opened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
    }

    private void CloseOriginalStore()
    {
        if (runtimeAcquisitionPending)
        {
            originalAcquisitionFailure?.Throw();
            throw new InvalidOperationException(AcquisitionNotJoined);
        }
        if (runtimeCloseAttempted)
        {
            originalCloseFailure?.Throw();
            if (store is not null)
            {
                throw new InvalidOperationException(CloseNotJoined);
            }
            return;
        }
        if (store is null)
        {
            return;
        }
        runtimeCloseAttempted = true;
        try
        {
            store.Dispose();
            store = null;
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
    }

    private void Cleanup(List<Exception> failures)
    {
        try
        {
            CloseOriginalStore();
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            RetainFailure(failures, failure);
            return;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            RetainFailure(failures, failure);
            return;
        }
        if (Directory.Exists(directory))
        {
            KeyLoad.Server.ServerFailureObserver.Observe(
                () => Directory.Delete(directory, recursive: true), failures);
        }
    }

    private static void RetainFailure(List<Exception> failures, Exception original)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, original)))
        {
            failures.Add(original);
        }
    }
}
