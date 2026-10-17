namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Names concrete invalid JSON transport fields without changing business arguments.</summary>
internal enum McpTransportFrameFault
{
    MethodMismatch, MethodWrongType, NameMismatch, NameWrongType, VersionMismatch, VersionWrongType, VersionNull,
    MetaMismatch, MetaWrongType, MetaNull
}
