namespace KeyLoad.Core.CommandOutcomes.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.CommandOutcomeScopeKind)]
internal enum CommandOutcomeScopeKind : int
{
    Unknown = 0,
    Global = 1,
    Partition = 2
}
