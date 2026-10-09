using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ConnectionRf3Reply<T>(T Value, Guid? RequestId);

internal sealed record ConnectionRf3HeldOperation<T>(Guid Arm, Task<T> Original,
    ConnectionProbeWitness Witness, RequestCqrsProbeMarkerRecord Marker);
