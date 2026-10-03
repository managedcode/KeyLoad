[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(KeyLoad.Features.InternalSerialization.NativePayloadVersion.TestAssembly)]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(KeyLoad.Features.InternalSerialization.NativePayloadVersion.ReplicationAssembly)]

namespace KeyLoad.Features.InternalSerialization;

internal static class NativePayloadVersion
{
    internal const uint Current = 1;
    internal const string TestAssembly = "KeyLoad.UnitTests";
    internal const string ReplicationAssembly = "KeyLoad.Replication";
    internal const string Alias = "keyload.internal.value.v1";
    internal const string InvalidPayload = "The internal binary payload is invalid.";
    internal const string UnsupportedVersion = "The internal binary payload version is unsupported.";
}

[Orleans.GenerateSerializer]
[Orleans.Alias(NativePayloadVersion.Alias)]
internal sealed class NativePayload
{
    [Orleans.Id(0)]
    public uint Version { get; init; }

    [Orleans.Id(1)]
    public object? Value { get; init; }
}
