namespace KeyLoad.Core.Features.CommandOutcomes.Execution;

internal readonly record struct CommandOutcomeSelection(byte[] Key, StoredOutcome? Outcome);
