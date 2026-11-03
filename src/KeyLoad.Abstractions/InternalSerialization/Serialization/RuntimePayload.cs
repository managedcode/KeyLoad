[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(KeyLoad.InternalSerialization.RuntimePayloadContract.TestAssembly)]

namespace KeyLoad.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimePayloadContract.Alias)]
internal sealed class RuntimePayload
{
    [Orleans.Id(1)]
    public object? /* KLD0041: Persisted keyload.internal.value field Id 1 carries runtime type headers and values in the current generated binary layout. */ Value
    {
        get; init;
    }
}
