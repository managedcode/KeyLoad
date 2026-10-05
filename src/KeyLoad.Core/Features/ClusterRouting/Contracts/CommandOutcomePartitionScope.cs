namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.CommandOutcomeScopeKind)]
internal enum CommandOutcomeScopeKind : int
{
    Unknown = 0,
    Global = 1,
    Partition = 2
}

internal readonly record struct CommandOutcomePartitionScope(CommandOutcomeScopeKind Kind, PartitionRef? Partition)
{
    internal static CommandOutcomePartitionScope Unknown => new(CommandOutcomeScopeKind.Unknown, null);
    internal static CommandOutcomePartitionScope Global => new(CommandOutcomeScopeKind.Global, null);
}
