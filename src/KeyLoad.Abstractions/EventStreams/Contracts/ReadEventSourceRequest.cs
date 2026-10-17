namespace KeyLoad;

/// <summary>Describes a bounded read from an event source.</summary>
/// <param name="Source">The source to read.</param>
/// <param name="AfterPosition">The exclusive source position from which to continue.</param>
/// <param name="Limit">The maximum number of events to return.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadEventSourceRequest)]
public sealed record ReadEventSourceRequest([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] long AfterPosition = ReadEventSourceRequest.DefaultAfterPosition, [property: Orleans.Id(2)] int Limit = ReadEventSourceRequest.DefaultLimit, [property: Orleans.Id(3)] string? Cursor = null)
{
    private const int DefaultAfterPosition = 0;
    private const int DefaultLimit = 100;
}
