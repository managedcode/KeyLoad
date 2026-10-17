namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal interface INativeCqrsLeafGrain : global::Orleans.IGrainWithGuidKey
{
    Task<NativeCqrsLeafObservation> ObserveAsync(Guid requestId);
}
