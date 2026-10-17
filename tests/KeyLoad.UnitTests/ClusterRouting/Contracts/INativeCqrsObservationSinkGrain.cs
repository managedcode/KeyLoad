namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal interface INativeCqrsObservationSinkGrain : global::Orleans.IGrainWithGuidKey
{
    Task RecordAsync(string eventName);
    Task RecordObservationAsync(NativeCqrsLeafObservation observation);
    Task RecordOutcomeAsync(bool succeeded);
    Task MarkSettledAsync(bool cancellationObserved);
}
