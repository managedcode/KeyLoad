using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryDatabase
{
    internal static DatabaseEngine Open(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(),
            RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(),
            RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(),
            RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(),
            KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
}
