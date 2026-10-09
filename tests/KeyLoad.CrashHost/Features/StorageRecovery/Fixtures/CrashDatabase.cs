using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class CrashDatabase
{
    private const string ThirdCrashVoterId = "crash-c";

    internal static DatabaseEngine Create(ZoneTreeStore store, bool boundOutbox = false,
        string tenantId = CrashFixtureValues.System)
    {
        const string CreateVoterIdsText = "crash-a";
        const string CreateCreateVoterIdsText = "crash-b";

        var database = new DatabaseEngine(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(boundOutbox),
            CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
            CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution(), CrashExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        database.Bootstrap(new(CrashFixtureValues.Principal, tenantId,
                [new(CrashFixtureValues.Wildcard, CrashFixtureValues.Wildcard, Capability.All)], [CrashFixtureValues.Wildcard])
        { ClusterAdministrator = true },
            DatabaseEngine.Credential(CrashFixtureValues.Principal, CrashFixtureValues.Principal, CrashFixtureValues.Credential));
        RecoveryPhysicalShardBootstrap.Bootstrap(database, CrashFixtureValues.Principal,
            [CreateVoterIdsText, CreateCreateVoterIdsText, ThirdCrashVoterId]);
        return database;
    }

    internal static ReplicatedOperation Operation<T>(OperationKind kind, T payload, Guid id)
        => new(id, kind, CrashFixtureValues.Principal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options));

    internal static OperationResult Submit<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
        => database.Apply(Operation(kind, payload, id));
}
