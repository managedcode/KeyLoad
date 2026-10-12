using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsProbeClaim(RequestCqrsProbeLoadedArm Arm, GrainRequestProbeIdentity Identity)
{
    internal Guid ConnectionId { get; init; }
    internal global::Orleans.Runtime.IGrainContext? QueryReadContext { get; set; }
    internal RequestCqrsProbeClaim? ReceiverIssueAdjunct { get; set; }
    internal long? EntryIndex { get; init; }
    internal long? EntryTerm { get; init; }
}
