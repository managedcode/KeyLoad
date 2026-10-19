using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests;

internal static class CrashDatabase
{
    internal static DatabaseEngine Create(ZoneTreeStore store, bool boundOutbox = false,
        string tenantId = CrashFixtureValues.System, DatabaseLimits? configuredLimits = null)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), configuredLimits is null ? CrashExecutionOptions.DatabaseLimits(boundOutbox) : CrashExecutionOptions.DatabaseLimits(configuredLimits),
            CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
            CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.RuntimeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
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

    internal static CommitToken NextCommitToken(DatabaseEngine database, PartitionRef partition)
        => database.Store.Read<CommitToken>(view =>
        {
            var placement = database.ReadAtomicPartitionPlacement(view, CrashFixtureValues.Principal, partition);
            var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                ? OrleansSerialization.Deserialize<long>(bytes) : 0L;
            return new(placement.Incarnation, partition.AtomicPartitionId, checked(applied + 1L));
        });

    internal static long AppliedPosition(IAtomicStore store)
        => store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : 0L);
}
