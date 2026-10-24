using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Creates the same real local engine configuration for initial open and cold reopen.</summary>
internal static class TestDatabaseEngineFactory
{
    private const string AllFields = "*";

    internal static Func<ZoneTreeStore, DatabaseEngine> Capture(IOptions<DatabaseLimits> limits,
        IOptions<ClaimsExecutionOptions> claims,
        TimeSeriesExecutionOptions? timeSeries, TimeProvider clock)
        => store => new DatabaseEngine(store, new AuthorizationPolicy(), limits,
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), claims,
            UnitExecutionOptions.TimeSeriesExecution(timeSeries), clock);

    internal static void Bootstrap(DatabaseEngine database, string principalId, string credential)
        => database.Bootstrap(new PrincipalRecord(principalId, AllFields,
            [new(AllFields, AllFields, Capability.All)], [AllFields])
        {
            ClusterAdministrator = true
        }, AuthorizationExecution.Credential(principalId, principalId, credential));
}
