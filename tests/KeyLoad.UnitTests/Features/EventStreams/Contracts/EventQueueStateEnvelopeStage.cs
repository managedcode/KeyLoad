namespace KeyLoad.UnitTests.Features.EventStreams;

internal enum EventQueueStateEnvelopeStage
{
    Produced,
    Claimed,
    Processed,
    ScheduledClaimed,
    Completed
}
