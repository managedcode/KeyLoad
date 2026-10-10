namespace KeyLoad;

/// <summary>Requested canonical resource scope; server catalog and authorization determine its sources.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(EventFeedScope.SerializerAlias)]
public sealed record EventFeedScope
{
    internal const string SerializerAlias = "keyload.event-feed-scope.v1";
    /// <summary>Gets the transaction domain selecting the source owners.</summary>
    [Orleans.Id(0)] public string Domain { get; init; } = null!;
    /// <summary>Gets the exact source resource authorization scope.</summary>
    [Orleans.Id(1)] public string Resource { get; init; } = null!;
    /// <summary>Gets the selected canonical event source kind.</summary>
    [Orleans.Id(2)] public EventSourceKind Kind { get; init; }
}
