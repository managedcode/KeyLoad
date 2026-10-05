using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Identifies one generation of an event stream.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="StreamSet">Identifies the event stream set.</param>
/// <param name="StreamId">Identifies the event stream.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StreamRef)]
public sealed record StreamRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string StreamSet, [property: Orleans.Id(2)] string StreamId, [property: Orleans.Id(3)] long Generation = StreamRef.DefaultGeneration)
{
    private const int DefaultGeneration = 1;
}

/// <summary>Describes the current tail, retention floor, and generation of a stream.</summary>
/// <param name="TailRevision">Identifies the current stream tail.</param>
/// <param name="FirstAvailableRevision">Identifies the earliest retained revision.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StreamHead)]
public sealed record StreamHead([property: Orleans.Id(0)] long TailRevision, [property: Orleans.Id(1)] long FirstAvailableRevision, [property: Orleans.Id(2)] long Generation);

/// <summary>Represents one persisted event and its position in a stream.</summary>
/// <param name="Stream">Specifies the stream value.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="EventSequence">Identifies the event sequence.</param>
/// <param name="Data">Contains the event data.</param>
/// <param name="RecordedAt">Records the persistence time.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventRecord)]
public sealed record EventRecord([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] long EventSequence, [property: Orleans.Id(3)] EventData Data, [property: Orleans.Id(4)] DateTimeOffset RecordedAt);

/// <summary>Returns a bounded event page with the observed head and committed cut.</summary>
/// <param name="Stream">Specifies the stream value.</param>
/// <param name="Head">Reports the stream head observed for this page.</param>
/// <param name="Events">Lists events to append.</param>
/// <param name="CutPosition">Identifies the committed read cut.</param>
/// <param name="HasMore">Indicates whether more events are available.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StreamPage)]
public sealed record StreamPage([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] StreamHead Head, [property: Orleans.Id(2)] ImmutableArray<EventRecord> Events, [property: Orleans.Id(3)] long CutPosition, [property: Orleans.Id(4)] bool HasMore);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ExpectedStreamRevision)]
public sealed record ExpectedStreamRevision([property: Orleans.Id(0)] ExpectedStreamState State, [property: Orleans.Id(1)] long Revision = ExpectedStreamRevision.DefaultRevision)
{
    private const int DefaultRevision = 0;

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventData)]
public sealed record EventData([property: Orleans.Id(0)] string EventId, [property: Orleans.Id(1)] string EventType, [property: Orleans.Id(2)] string PayloadJson, [property: Orleans.Id(3)] string HeadersJson = EventData.DefaultHeadersJson,
    [property: Orleans.Id(4)] int SchemaVersion = EventData.InitialSchemaVersion, [property: Orleans.Id(5)] DateTimeOffset? OccurredAt = null, [property: Orleans.Id(6)] string? CorrelationId = null, [property: Orleans.Id(7)] string? CausationId = null)
{
    private const string DefaultHeadersJson = "{}";
    private const int InitialSchemaVersion = 1;
}

/// <summary>Appends events to a stream under an expected revision and generation.</summary>
/// <param name="StreamSet">Identifies the event stream set.</param>
/// <param name="StreamId">Identifies the event stream.</param>
/// <param name="Events">Lists events to append.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AppendEvents)]
public sealed record AppendEvents([property: Orleans.Id(0)] string StreamSet, [property: Orleans.Id(1)] string StreamId, [property: Orleans.Id(2)] ImmutableArray<EventData> Events,
    [property: Orleans.Id(3)] ExpectedStreamRevision ExpectedRevision, [property: Orleans.Id(4)] long Generation = AppendEvents.DefaultGeneration) : Mutation(StreamSet)
{
    private const int DefaultGeneration = 1;
}
