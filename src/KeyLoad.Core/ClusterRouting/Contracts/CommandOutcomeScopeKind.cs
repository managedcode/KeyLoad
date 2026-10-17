namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.CommandOutcomeScopeKind)]
internal enum CommandOutcomeScopeKind : int
{
    Unknown = 0,
    Global = 1,
    Partition = 2
}
