namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal readonly record struct CommandOutcomeSelection(byte[] Key, StoredOutcome? Outcome);
