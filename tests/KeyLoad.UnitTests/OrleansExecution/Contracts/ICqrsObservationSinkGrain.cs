namespace KeyLoad.UnitTests.Features.OrleansExecution;

[global::Orleans.CodeGeneration.Version(1)]
internal interface ICqrsObservationSinkGrain : global::Orleans.IGrainWithGuidKey
{
    Task RecordAsync(string eventName);
    Task RecordObservationAsync(CqrsLeafObservation observation);
    Task RecordOutcomeAsync(bool succeeded);
    Task MarkSettledAsync(bool cancellationObserved);
}
