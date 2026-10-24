using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.EventStreams;

internal static class ProjectionRecoveryDatabase
{
    [ConfigurationBinding]
    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(new() { MaxOutboxRecords = 1 }),
        RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(),
        RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.RuntimeClaimsExecution(),
        RecoveryExecutionOptions.TimeSeriesExecution());
}
