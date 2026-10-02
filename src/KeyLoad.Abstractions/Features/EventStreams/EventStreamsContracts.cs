using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Identifies one generation of an event stream.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="StreamSet">Identifies the event stream set.</param>
/// <param name="StreamId">Identifies the event stream.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
public sealed record StreamRef(PartitionRef Partition, string StreamSet, string StreamId, long Generation = 1);

/// <summary>Describes the current tail, retention floor, and generation of a stream.</summary>
/// <param name="TailRevision">Identifies the current stream tail.</param>
/// <param name="FirstAvailableRevision">Identifies the earliest retained revision.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
public sealed record StreamHead(long TailRevision, long FirstAvailableRevision, long Generation);

/// <summary>Represents one persisted event and its position in a stream.</summary>
/// <param name="Stream">Specifies the stream value.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="EventSequence">Identifies the event sequence.</param>
/// <param name="Data">Contains the event data.</param>
/// <param name="RecordedAt">Records the persistence time.</param>
public sealed record EventRecord(StreamRef Stream, long Revision, long EventSequence, EventData Data, DateTimeOffset RecordedAt);

/// <summary>Returns a bounded event page with the observed head and committed cut.</summary>
/// <param name="Stream">Specifies the stream value.</param>
/// <param name="Head">Reports the stream head observed for this page.</param>
/// <param name="Events">Lists events to append.</param>
/// <param name="CutPosition">Identifies the committed read cut.</param>
/// <param name="HasMore">Indicates whether more events are available.</param>
public sealed record StreamPage(StreamRef Stream, StreamHead Head, ImmutableArray<EventRecord> Events, long CutPosition, bool HasMore);

/// <summary>Selects the expected existence condition for an event stream.</summary>
public enum ExpectedStreamState
{
    /// <summary>Require the exact supplied revision.</summary>
    Exact,
    /// <summary>Require that no stream currently exists.</summary>
    NoStream,
    /// <summary>Accept any current stream state.</summary>
    Any
}

/// <summary>Describes the expected stream state for an append operation.</summary>
/// <param name="State">Describes the expected stream state.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
public sealed record ExpectedStreamRevision(ExpectedStreamState State, long Revision = 0)
{
    /// <summary>Creates an exact-revision stream precondition.</summary>
    /// <returns>The requested value.</returns>
    public static ExpectedStreamRevision Exact(long revision) => new(ExpectedStreamState.Exact, revision);
    /// <summary>Gets a precondition requiring the stream to be absent.</summary>
    public static ExpectedStreamRevision NoStream { get; } = new(ExpectedStreamState.NoStream);
    /// <summary>Gets a precondition accepting any current stream state.</summary>
    public static ExpectedStreamRevision Any { get; } = new(ExpectedStreamState.Any);
}

/// <summary>Carries event identity, type, payload, headers, and occurrence metadata.</summary>
/// <param name="EventId">Identifies the event.</param>
/// <param name="EventType">Names the event type.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="SchemaVersion">Identifies the payload schema version.</param>
/// <param name="OccurredAt">Records the optional event occurrence time.</param>
/// <param name="CorrelationId">Carries the optional correlation identifier.</param>
/// <param name="CausationId">Carries the optional causation identifier.</param>
public sealed record EventData(string EventId, string EventType, string PayloadJson, string HeadersJson = "{}",
    int SchemaVersion = 1, DateTimeOffset? OccurredAt = null, string? CorrelationId = null, string? CausationId = null);

/// <summary>Appends events to a stream under an expected revision and generation.</summary>
/// <param name="StreamSet">Identifies the event stream set.</param>
/// <param name="StreamId">Identifies the event stream.</param>
/// <param name="Events">Lists events to append.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
public sealed record AppendEvents(string StreamSet, string StreamId, ImmutableArray<EventData> Events,
    ExpectedStreamRevision ExpectedRevision, long Generation = 1) : Mutation(StreamSet);
