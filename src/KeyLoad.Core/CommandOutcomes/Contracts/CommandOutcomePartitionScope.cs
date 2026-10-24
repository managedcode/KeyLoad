namespace KeyLoad.Core.CommandOutcomes.Contracts;

internal readonly record struct CommandOutcomePartitionScope(CommandOutcomeScopeKind Kind, PartitionRef? Partition)
{
    internal static CommandOutcomePartitionScope Unknown => new(CommandOutcomeScopeKind.Unknown, null);
    internal static CommandOutcomePartitionScope Global => new(CommandOutcomeScopeKind.Global, null);
}
