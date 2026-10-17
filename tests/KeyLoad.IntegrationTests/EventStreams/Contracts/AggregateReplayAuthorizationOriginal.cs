namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed record AggregateReplayAuthorizationOriginal(ReadAggregateReplayRequest Request,
    CommandRequest SnapshotCommand, CommitReceipt Receipt, AggregateReplayPage Page);
