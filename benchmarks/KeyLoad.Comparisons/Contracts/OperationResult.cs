using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Holds the optional outputs produced while executing one workload operation.</summary>
/// <param name="Document">The document returned by a point-read operation, when present.</param>
/// <param name="Neighbors">The documents returned by a neighbor-search operation, when present.</param>
/// <param name="Message">The message returned by a queue operation, when present.</param>
/// <param name="Queue">The measured queue phase durations, when present.</param>
/// <param name="Vertices">The vertex identifiers returned by a traversal, when present.</param>
/// <param name="Event">The event returned by a stream operation, when present.</param>
public sealed record OperationResult(FoundDocument? Document = null, ImmutableArray<FoundDocument>? Neighbors = null,
    FoundDocument? Message = null, QueueTimings? Queue = null, ImmutableArray<string>? Vertices = null, FoundEvent? Event = null);
