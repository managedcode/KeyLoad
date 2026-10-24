namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record DeclarativeCompositionBudgetState(CommandRequest Refused, CommandRequest Command,
    CommitReceipt Receipt, MessageInspection First, MessageInspection Second, global::KeyLoad.GraphTraversal Graph);
