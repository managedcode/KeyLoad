using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Complete authorized native event feed parent result.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(EventFeedControlResult.SerializerAlias)]
public sealed record EventFeedControlResult
{
    internal const string SerializerAlias = "keyload.event-feed-control-result.v1";
    internal const int VersionField = 0;
    /// <summary>Gets the versioned complete event feed result.</summary>
    [Orleans.Id(VersionField)] public int Version { get; init; }
    internal const int PublicCommandIdField = 1;
    /// <summary>Gets the original public parent command identity.</summary>
    [Orleans.Id(PublicCommandIdField)] public Guid PublicCommandId { get; init; }
    internal const int MapIdField = 2;
    /// <summary>Gets the actual canonical vector map identity.</summary>
    [Orleans.Id(MapIdField)] public Guid MapId { get; init; }
    internal const int RevisionField = 3;
    /// <summary>Gets the actual canonical map revision, or zero for issuance cleanup.</summary>
    [Orleans.Id(RevisionField)] public long Revision { get; init; }
    internal const int CoverageGenerationField = 4;
    /// <summary>Gets the actual source coverage generation, or zero for issuance cleanup.</summary>
    [Orleans.Id(CoverageGenerationField)] public long CoverageGeneration { get; init; }
    internal const int ReleasedField = 5;
    /// <summary>Gets the whether actual retained ownership was fully released.</summary>
    [Orleans.Id(ReleasedField)] public bool Released { get; init; }
    internal const int RetainedField = 6;
    /// <summary>Gets the whether canonical map or source ownership remains charged.</summary>
    [Orleans.Id(RetainedField)] public bool Retained { get; init; }
    internal const int CursorField = 7;
    /// <summary>Gets the actual signed native map cursor.</summary>
    [Orleans.Id(CursorField)] public string? Cursor { get; init; }
    internal const int OfferDigestField = 8;
    /// <summary>Gets the actual signed native offer digest.</summary>
    [Orleans.Id(OfferDigestField)] public string? OfferDigest { get; init; }
    internal const int PagesField = 9;
    /// <summary>Gets the complete original authenticated source pages for the offer.</summary>
    [Orleans.Id(PagesField)] public ImmutableArray<EventSourcePage> Pages { get; init; } = ImmutableArray<EventSourcePage>.Empty;
    internal const int ReceiptField = 10;
    /// <summary>Gets the genuine terminal native receipt with its own command identity and cut.</summary>
    [Orleans.Id(ReceiptField)] public CommitReceipt Receipt { get; init; } = null!;
}
