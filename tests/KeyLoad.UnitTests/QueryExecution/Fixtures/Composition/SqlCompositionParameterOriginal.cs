namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlCompositionParameterOriginal(CommandRequest Command, ReplicatedOperation Operation,
    CommitReceipt Expected, Mutation Effect);
