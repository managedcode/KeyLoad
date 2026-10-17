using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record PostDispatchAdmissionReturnState(bool Disconnected, ConnectionRf3Caller OriginalCaller,
    CommandRequest Command, ConnectionProbeWitness OriginalOwner, OutboxStatus Before);
