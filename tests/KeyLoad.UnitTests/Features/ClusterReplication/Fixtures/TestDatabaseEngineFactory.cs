using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class TestDatabaseEngineFactory
{
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
