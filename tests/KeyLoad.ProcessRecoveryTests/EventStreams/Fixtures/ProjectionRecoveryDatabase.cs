using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class ProjectionRecoveryDatabase
{
    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(),
        RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(),
        RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.RuntimeClaimsExecution(),
        RecoveryExecutionOptions.TimeSeriesExecution());
}
