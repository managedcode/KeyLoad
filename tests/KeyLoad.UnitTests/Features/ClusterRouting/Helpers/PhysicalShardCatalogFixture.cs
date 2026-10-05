using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Security;
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
    private ZoneTreeStore? store;

    internal PhysicalShardCatalogFixture()
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
        Store.Dispose();
        store = null;
        OpenExisting();
    }

    private void Initialize()
    {
        OpenExisting();
        Database.Bootstrap(new PrincipalRecord(RootPrincipalId, TenantId,
            [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true },
            DatabaseEngine.Credential(RootPrincipalId, RootPrincipalId, RootCredential));
    }

    private void OpenExisting()
    {
        var opened = new ZoneTreeStore(new(directory));
        store = opened;
        Database = new(opened, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        Cleanup(failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Cleanup(List<Exception> failures)
    {
        if (store is not null)
        {
            try
            {
                store.Dispose();
                store = null;
            }
            catch (Exception failure) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { failures.Add(failure); return; }
            catch (Exception failure) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { failures.Add(failure); return; }
        }

        if (Directory.Exists(directory))
        {
            KeyLoad.Server.ServerFailureObserver.Observe(
                () => Directory.Delete(directory, recursive: true), failures);
        }
    }
}
