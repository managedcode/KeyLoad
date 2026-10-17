using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ConnectionQueryContextRead(QueryPage Page, ConnectionProbeWitness Witness, RequestCqrsProbeMarkerRecord Marker);
