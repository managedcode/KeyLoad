using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

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
