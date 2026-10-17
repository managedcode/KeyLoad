using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AllowClientCall]
internal interface INativeCqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<NativeCqrsObservationSnapshot> ReadAsync();
}
