[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(KeyLoad.Features.InternalSerialization.NativePayloadVersion.TestAssembly)]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(KeyLoad.Features.InternalSerialization.NativePayloadVersion.ReplicationAssembly)]

namespace KeyLoad.Features.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(NativePayloadVersion.Alias)]
internal sealed class NativePayload
{
    [Orleans.Id(0)]
    public uint Version
    {
        get; init;
    }

    [Orleans.Id(1)]
    public object? Value
    {
        get; init;
    }
}
