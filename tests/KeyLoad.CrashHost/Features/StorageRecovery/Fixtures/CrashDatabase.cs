using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class CrashDatabase
{
    internal static DatabaseEngine Create(ZoneTreeStore store, bool boundOutbox = false,
        string tenantId = CrashFixtureValues.System)
    {
        var database = boundOutbox
            ? new DatabaseEngine(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(new() { MaxOutboxRecords = 1 }), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.TimeSeriesExecution())
            : new DatabaseEngine(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.TimeSeriesExecution());
        database.Bootstrap(new(CrashFixtureValues.Principal, tenantId,
                [new(CrashFixtureValues.Wildcard, CrashFixtureValues.Wildcard, Capability.All)], [CrashFixtureValues.Wildcard])
        { ClusterAdministrator = true },
            DatabaseEngine.Credential(CrashFixtureValues.Principal, CrashFixtureValues.Principal, CrashFixtureValues.Credential));
        RecoveryPhysicalShardBootstrap.Bootstrap(database, CrashFixtureValues.Principal,
            ["crash-a", "crash-b", "crash-c"]);
        return database;
    }

    internal static ReplicatedOperation Operation<T>(OperationKind kind, T payload, Guid id)
        => new(id, kind, CrashFixtureValues.Principal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options));

    internal static OperationResult Submit<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
        => database.Apply(Operation(kind, payload, id));
}
