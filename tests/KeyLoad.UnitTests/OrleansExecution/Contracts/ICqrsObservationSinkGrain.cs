namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal interface ICqrsObservationSinkGrain : global::Orleans.IGrainWithGuidKey
{
    Task RecordAsync(string eventName);
    Task RecordObservationAsync(CqrsLeafObservation observation);
    Task RecordOutcomeAsync(bool succeeded);
    Task MarkSettledAsync(bool cancellationObserved);
}
