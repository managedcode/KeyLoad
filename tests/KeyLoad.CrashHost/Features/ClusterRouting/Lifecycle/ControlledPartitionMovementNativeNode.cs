using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Owns known canonical and replica roots for one actual movement physical owner.</summary>
internal sealed class ControlledPartitionMovementNativeNode : IDisposable
{
    private const string CanonicalDirectory = "database";
    private const string ReplicaDirectory = "replica";
    private const string InfrastructurePrincipal = "root";
    private const string SystemTenant = "system";
    private const string Wildcard = "*";
    private const string InfrastructureCredential = "root.movement-native-fixture-credential-32-characters";
    private bool closed;

    /// <summary>Opens the actual configured scope and composes its original native command owner.</summary>
    /// <param name="root">The physical owner root, owned by the process trial.</param>
    /// <param name="owner">The actual listener-configured persisted physical owner.</param>
    /// <param name="observer">The existing storage observer for an armed actual canonical commit cut.</param>
    public ControlledPartitionMovementNativeNode(string root, PhysicalShardRecord owner,
        Action<CommitStage, long, int>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Root = Path.GetFullPath(root);
        Store = new(new(Path.Combine(Root, CanonicalDirectory))
        { Incarnation = owner.Incarnation, FaultObserver = observer },
            CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        try
        {
            Database = new(Store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
                CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
                CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(),
                CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(),
                CrashExecutionOptions.TimeSeriesExecution(), physicalOwner: owner);
            Database.Bootstrap(new(InfrastructurePrincipal, SystemTenant,
                [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
            { ClusterAdministrator = true },
                DatabaseEngine.Credential(InfrastructurePrincipal, InfrastructurePrincipal, InfrastructureCredential));
            Journal = new(Database, owner, Path.Combine(Root, ReplicaDirectory));
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { Store.Dispose(); }
            catch (Exception cleanup) when (CqrsRuntimeFailures.FindFatal(cleanup) is null) { failures.Add(cleanup); }
            catch (Exception cleanup) when (CqrsRuntimeFailures.FindFatal(cleanup) is not null) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    /// <summary>The exact owned physical directory used for current native configuration.</summary>
    public string Root { get; }
    /// <summary>The actual canonical node-local store.</summary>
    public ZoneTreeStore Store { get; }
    /// <summary>The actual database with only locally bootstrapped infrastructure authority.</summary>
    public DatabaseEngine Database { get; }
    /// <summary>The original joined durable native command path for every subsequent effect.</summary>
    public ControlledPartitionMovementNativeJournal Journal { get; }

    /// <summary>Joins the original journal/materializer before disposing canonical ownership.</summary>
    public void Dispose()
    {
        if (closed)
        { return; }
        closed = true;
        var failures = new List<Exception>();
        try
        { Journal.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        try
        { Store.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
