using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class TestDatabaseEngineFactory
{
    private const string SystemTenantId = "system";
    private const string Wildcard = "*";

    internal static void Bootstrap(DatabaseEngine database, string rootPrincipal, string rootCredential, bool physicalCatalog)
    {
        database.Bootstrap(new(rootPrincipal, SystemTenantId, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        {
            ClusterAdministrator = true
        }, DatabaseEngine.Credential(rootPrincipal, rootPrincipal, rootCredential));
        if (physicalCatalog)
        {
            Features.ClusterRouting.PhysicalShardTestBootstrap.Bootstrap(database, rootPrincipal);
        }
    }

    internal static Func<ZoneTreeStore, PhysicalShardRecord?, DatabaseEngine> Capture(IOptions<DatabaseLimits> databaseLimits,
        IOptions<BlobExecutionOptions> blob, IOptions<NativeClaimsExecutionOptions> claims,
        TimeSeriesExecutionOptions? series, IOptions<PartitionMovementCheckpointOptions> movement, TimeProvider clock)
    {
        var authorization = new AuthorizationPolicy();
        var due = UnitExecutionOptions.DueWork();
        var events = UnitExecutionOptions.EventSource();
        var messaging = UnitExecutionOptions.Messaging();
        var graph = UnitExecutionOptions.GraphExecution();
        var changes = UnitExecutionOptions.ChangeFeedExecution();
        var timeSeries = UnitExecutionOptions.TimeSeriesExecution(series);
        return (store, owner) => new(store, authorization, databaseLimits, due, events, messaging, graph, changes,
            blob, claims, timeSeries, movement, UnavailablePartitionMovementCheckpointVerifier.Instance, clock, owner);
    }
}
