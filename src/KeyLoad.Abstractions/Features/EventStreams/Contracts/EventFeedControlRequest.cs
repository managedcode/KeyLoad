namespace KeyLoad;

/// <summary>Untrusted public inputs to an authorized canonical event-feed operation.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(EventFeedControlRequest.SerializerAlias)]
public sealed record EventFeedControlRequest
{
    internal const string SerializerAlias = "keyload.event-feed-control-request.v1";
    /// <summary>Gets the versioned event feed control contract.</summary>
    [Orleans.Id(0)] public int Version { get; init; }
    /// <summary>Gets the immutable public operation identity.</summary>
    [Orleans.Id(1)] public Guid CommandId { get; init; }
    /// <summary>Gets the atomic partition owning the control state.</summary>
    [Orleans.Id(2)] public PartitionRef ControlPartition { get; init; } = null!;
    /// <summary>Gets the native vector map identity.</summary>
    [Orleans.Id(3)] public Guid MapId { get; init; }
    /// <summary>Gets the requested control operation.</summary>
    [Orleans.Id(4)] public EventFeedControlAction Action { get; init; }
    /// <summary>Gets the requested source selection and resource authority.</summary>
    [Orleans.Id(5)] public EventFeedScope Scope { get; init; } = null!;
    /// <summary>Gets the initial source position policy.</summary>
    [Orleans.Id(6)] public EventFeedStartPolicy Start { get; init; }
    /// <summary>Gets the expected control revision for compare and set.</summary>
    [Orleans.Id(7)] public long ExpectedRevision { get; init; }
    /// <summary>Gets the expected captured coverage generation.</summary>
    [Orleans.Id(8)] public long ExpectedCoverageGeneration { get; init; }
    /// <summary>Gets the original signed continuation cursor.</summary>
    [Orleans.Id(9)] public string? Cursor { get; init; }
    /// <summary>Gets the original coverage offer digest.</summary>
    [Orleans.Id(10)] public string? OfferDigest { get; init; }
    /// <summary>Gets the requested bounded page size.</summary>
    [Orleans.Id(11)] public int Limit { get; init; }
}
