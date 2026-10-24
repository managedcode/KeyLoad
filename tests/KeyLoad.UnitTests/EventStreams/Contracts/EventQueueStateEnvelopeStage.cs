namespace KeyLoad.UnitTests.EventStreams;

internal enum EventQueueStateEnvelopeStage
{
    Produced,
    Claimed,
    Processed,
    ScheduledClaimed,
    Completed
}
