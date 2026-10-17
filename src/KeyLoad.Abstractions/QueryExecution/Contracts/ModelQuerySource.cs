namespace KeyLoad.Query;

/// <summary>Identifies an authorized read-only database model source.</summary>
public enum ModelQuerySourceKind
{
    /// <summary>Reads retained records of one event stream generation.</summary>
    Events,
    /// <summary>Inspects retained metadata and bodies of one queue lane.</summary>
    QueueMessages,
    /// <summary>Reads retained records of one topic generation.</summary>
    TopicEvents
}

/// <summary>Binds a model source within the query's partition and configured resource.</summary>
/// <param name="Kind">The read-only source model.</param>
/// <param name="Item">The event stream identifier or the query's same queue/topic resource name.</param>
/// <param name="Generation">The stream/topic generation; queue sources require one.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ModelQuerySource)]
public sealed record ModelQuerySource([property: Orleans.Id(0)] ModelQuerySourceKind Kind,
    [property: Orleans.Id(1)] string Item, [property: Orleans.Id(2)] long Generation = ModelQuerySource.DefaultGeneration)
{
    private const int DefaultGeneration = 1;
}
