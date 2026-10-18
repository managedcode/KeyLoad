using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryHost;

internal static class CrashDatabase
{
    internal static DatabaseEngine Create(ZoneTreeStore store, bool boundOutbox = false,
        string tenantId = CrashFixtureValues.System, DatabaseLimits? configuredLimits = null)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), configuredLimits is null ? CrashExecutionOptions.DatabaseLimits(boundOutbox) : CrashExecutionOptions.DatabaseLimits(configuredLimits),
            CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
            CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
        database.Bootstrap(new(CrashFixtureValues.Principal, tenantId,
                [new(CrashFixtureValues.Wildcard, CrashFixtureValues.Wildcard, Capability.All)], [CrashFixtureValues.Wildcard])
        {
            ClusterAdministrator = true
        },
            DatabaseEngine.Credential(CrashFixtureValues.Principal, CrashFixtureValues.Principal, CrashFixtureValues.Credential));
        return database;
    }

    internal static Command<DatabaseCommand> Operation<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
        => database.CreateCommand(kind, id, CrashFixtureValues.Principal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options));

    internal static Result<object> Submit<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
        => database.Apply(Operation(database, kind, payload, id));
}
