namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed record StreamTraversalRf3State(McpEventStreamScenario Scenario,
    CommandRequest Append, CommitReceipt Original, CommandRequest FourthCommand, CommitReceipt Fourth,
    StreamPage First, StreamPage Backward, NodeStatus Before);
