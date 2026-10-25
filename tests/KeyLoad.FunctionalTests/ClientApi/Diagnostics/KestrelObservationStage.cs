namespace KeyLoad.FunctionalTests.ClientApi;

internal enum KestrelObservationStage
{
    HostStarting, HostStarted, SendStarted, ResponseReceived, SendFailed,
    HandlerEntered, HandlerCompleted, HandlerFailed, SdkCompleted, CallerCancelled, FlowFailed
}
