namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainServiceClient
{
    internal Task<CommandObservationNativeReply> ExecuteOriginalCommandObservationPacketAsync(SiloAddress destination,
        CommandObservationNativeRequest request, CancellationToken token)
        => GetGrainService(destination).ExecuteOriginalCommandObservationAsync(request, token);
}
