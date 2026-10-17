namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainService
{
    private CommandObservationNativeEndpoint? commandObservation;

    public Task<CommandObservationNativeReply> ExecuteOriginalCommandObservationAsync(
        CommandObservationNativeRequest request, CancellationToken token)
        => (commandObservation ?? throw Errors.Fail(ErrorCode.OwnershipLost,
            CommandObservationNativeProtocol.Unavailable)).ExecuteAsync(request, token);
}
