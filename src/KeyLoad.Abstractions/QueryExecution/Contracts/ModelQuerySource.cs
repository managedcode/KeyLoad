namespace KeyLoad.Query;

/// <summary>Binds a model source within the query's partition and configured resource.</summary>
/// <param name="Kind">The read-only source model.</param>
/// <param name="Item">The event stream identifier or the query's same queue/topic resource name.</param>
/// <param name="Generation">The stream/topic generation; queue sources require one.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ModelQuerySource)]
public sealed record ModelQuerySource([property: Orleans.Id(0)] ModelQuerySourceKind Kind,
    [property: Orleans.Id(1)] string Item, [property: Orleans.Id(2)] long Generation = ModelQuerySource.DefaultGeneration)
{
    private const int DefaultGeneration = 1;
}
