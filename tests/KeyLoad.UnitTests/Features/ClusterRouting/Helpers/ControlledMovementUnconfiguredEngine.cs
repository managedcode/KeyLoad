using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledMovementUnconfiguredEngine
{
    internal static DatabaseEngine Create(IAtomicStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
}
