using System.Collections.Immutable;

namespace KeyLoad.Core.DocumentStorage;

/// <summary>Keeps complete local omission lineage separate from the caller-visible document.</summary>
internal sealed record DocumentProjection(DocumentResult Value, ImmutableArray<string> CompleteOmittedPaths);
