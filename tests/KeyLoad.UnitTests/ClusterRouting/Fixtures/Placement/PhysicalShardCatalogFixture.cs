using System.Runtime.ExceptionServices;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogFixture : IDisposable
{
    internal const string RootPrincipalId = "root";
    private const string TenantId = "system";
    private const string RootCredential = "root.unit-test-credential-32-characters";
    private const string DirectoryPrefix = "keyload-physical-shard-catalog-";
    private const string GuidFormat = "N";
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private readonly Guid? incarnation;
    private readonly PhysicalShardRecord? physicalOwner;
    private ZoneTreeStore? store;
    private bool nativeAcquisitionPending;
    private bool nativeCloseAttempted;
    private ExceptionDispatchInfo? originalAcquisitionFailure;
    private ExceptionDispatchInfo? originalCloseFailure;
    private const string AcquisitionNotJoined = "The native catalog owner acquisition is unknown; retain its root.";
    private const string CloseNotJoined = "The original native catalog owner close is unknown; retain its root.";

    internal PhysicalShardCatalogFixture(Guid? incarnation = null, PhysicalShardRecord? physicalOwner = null)
    {
        this.incarnation = incarnation;
        this.physicalOwner = physicalOwner;
        var failures = new List<Exception>();
        KeyLoad.Server.ServerFailureObserver.Observe(Initialize, failures);
        if (failures.Count == 0)
        {
            return;
        }

        Cleanup(failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal ZoneTreeStore Store => store ?? throw new ObjectDisposedException(nameof(PhysicalShardCatalogFixture));
    internal DatabaseEngine Database { get; private set; } = null!;

    internal OperationResult Bootstrap(BootstrapPhysicalShardCatalogRequest request, Guid? commandId = null)
    {
        var operation = Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            commandId ?? PhysicalShardCatalogIdentity.CreateBootstrapCommandId(request.PhysicalShardId),
            RootPrincipalId, Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        return Database.Apply(operation);
    }

    internal void AddPrincipal(PrincipalRecord principal)
        => Database.Apply(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal, RootPrincipalId,
            Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(new ConfigurePrincipalRequest(principal), JsonDefaults.Options)));

    internal OperationResult BootstrapPublic(BootstrapPhysicalShardCatalogRequest request)
        => Database.Apply(new(Guid.NewGuid(), OperationKind.BootstrapPhysicalShardCatalog, RootPrincipalId,
            Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options)));

    internal byte[] ReadCatalogBytes()
    {
        byte[]? bytes = null;
        Store.Read(view => view.ReadValue(PhysicalShardCatalogRecordSerialization.CatalogKey(),
            value => bytes = value.ToArray()));
        return bytes ?? throw new InvalidOperationException("The physical shard catalog row is missing.");
    }

    internal void ReplaceCatalog(PhysicalShardCatalog catalog)
    {
        using var encoder = new NativeSerializerFixture();
        ReplaceCatalogBytes(encoder.Encode(catalog));
    }

    internal void ReplaceCatalogBytes(byte[] bytes)
        => Store.Commit((transaction, _) =>
        {
            transaction.Put(PhysicalShardCatalogRecordSerialization.CatalogKey(), bytes);
            return true;
        });

    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(store is null && !nativeAcquisitionPending, this);
        CloseOriginalStore();
        OpenExisting();
    }

    private void Initialize()
    {
        OpenExisting();
        Database.Bootstrap(new PrincipalRecord(RootPrincipalId, TenantId,
            [new("*", "*", Capability.All)], ["*"])
        {
            ClusterAdministrator = true
        },
            DatabaseEngine.Credential(RootPrincipalId, RootPrincipalId, RootCredential));
    }

    private void OpenExisting()
    {
        var options = new ZoneTreeStoreOptions(directory)
        {
            Incarnation = incarnation
        };
        var execution = UnitExecutionOptions.StorageExecution();
        var cache = UnitExecutionOptions.PointCacheExecution();
        nativeAcquisitionPending = true;
        ZoneTreeStore opened;
        try
        {
            opened = new ZoneTreeStore(options, execution, cache);
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
        Database = new(opened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance, physicalOwner: physicalOwner);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        Cleanup(failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
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
