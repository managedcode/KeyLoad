using System.Runtime.ExceptionServices;
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

    private bool nativeAcquisitionPending;
    private bool nativeCloseAttempted;
    private ExceptionDispatchInfo? originalAcquisitionFailure;
    private ExceptionDispatchInfo? originalCloseFailure;
    private const string AcquisitionNotJoined = "The native runtime journal owner acquisition is unknown; retain its root.";
    private const string CloseNotJoined = "The original native runtime journal owner close is unknown; retain its root.";

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
        }, DatabaseEngine.Credential(RootKeyId, RootId, RootSecret));
        Database.ConfigureRuntimeJournal(Options.Create(options));
        Submit(new(RuntimeJournalAction.BootstrapIdentity, string.Empty, Guid.Empty, 0, 0, null,
            ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []), RootId);
    }

    internal RuntimeJournalMutationResult Submit(RuntimeJournalMutation mutation, string principal = "keyload-internal-runtime-journal")
        => Database.Apply(new(Guid.NewGuid(), OperationKind.RuntimeJournal, principal, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(mutation, JsonDefaults.Options))).Get<RuntimeJournalMutationResult>();

    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(store is null && !nativeAcquisitionPending, this);
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
        nativeAcquisitionPending = true;
        ZoneTreeStore opened;
        try
        {
            opened = new ZoneTreeStore(storageOptions, execution, cache);
            store = opened;
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        nativeAcquisitionPending = false;
        nativeCloseAttempted = false;
        originalAcquisitionFailure = null;
        originalCloseFailure = null;
        Database = new(opened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
    }

    private void CloseOriginalStore()
    {
        if (nativeAcquisitionPending)
        {
            originalAcquisitionFailure?.Throw();
            throw new InvalidOperationException(AcquisitionNotJoined);
        }
        if (nativeCloseAttempted)
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
        nativeCloseAttempted = true;
        try
        {
            store.Dispose();
            store = null;
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            RetainFailure(failures, failure);
            return;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
