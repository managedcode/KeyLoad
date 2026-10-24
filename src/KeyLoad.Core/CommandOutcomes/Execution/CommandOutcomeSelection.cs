namespace KeyLoad.Core.CommandOutcomes.Execution;

internal readonly record struct CommandOutcomeSelection(byte[] Key, StoredOutcome? Outcome);
