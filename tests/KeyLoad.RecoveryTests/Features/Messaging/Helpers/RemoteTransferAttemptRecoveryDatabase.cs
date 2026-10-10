using KeyLoad.Core;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferAttemptRecoveryDatabase
{
    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(new() { MaxQueueTransferAcceptAttempts = RemoteTransferAttemptCrashProtocol.Ceiling }),
        RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(),
        RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(),
        RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(),
        RecoveryExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
}
