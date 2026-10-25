namespace KeyLoad.FunctionalTests.EventStreams;

internal enum EventQueueStateEnvelopeStage
{
    Produced,
    Claimed,
    Processed,
    ScheduledClaimed,
    Completed
}
