[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(KeyLoad.Features.InternalSerialization.PayloadVersion.TestAssembly)]

namespace KeyLoad.Features.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(PayloadVersion.Alias)]
internal sealed class RuntimePayload
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
