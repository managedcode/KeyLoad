using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class RemoteTransferRepairRecoveryDatabase
{
    [ConfigurationBinding]
    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(new()
        {
            MaxQueueTransferRepairAttempts = RemoteTransferRepairCrashProtocol.Ceiling
        }),
        RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(),
        RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.RuntimeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
}
