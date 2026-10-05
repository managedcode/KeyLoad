namespace KeyLoad.Core;

/// <summary>Shared node-owned admission limits for shortest paths and graph-assisted search.</summary>
[ConfigurationOptions]
public sealed record GraphExecutionOptions
{
    /// <summary>The centrally bound graph execution section.</summary>
    public const string SectionName = "KeyLoad:GraphExecution";
    /// <summary>Safe startup rejection for settings outside the qualified traversal bounds.</summary>
    public const string ValidationMessage = "Graph execution limits must remain within the supported traversal bounds.";

    private const int DefaultMaximumDepth = 16;
    private const int DefaultMaximumVertices = 10_000;
    private const int DefaultMaximumEdges = 50_000;
    private const int DefaultMaximumLabels = 64;
    private const int MaximumConformingDepth = 16;
    private const int MaximumConformingVertices = 10_000;
    private const int MaximumConformingEdges = 50_000;
    private const int MaximumConformingLabels = 64;
    private const int MinimumDepth = 0;
    private const int MinimumWorkCount = 1;

    /// <summary>Gets the maximum admitted graph edge depth, including zero-hop work.</summary>
    public int MaximumDepth { get; init; } = DefaultMaximumDepth;
    /// <summary>Gets the maximum admitted visible vertices.</summary>
    public int MaximumVertices { get; init; } = DefaultMaximumVertices;
    /// <summary>Gets the maximum examined adjacency edges, including hidden or filtered edges.</summary>
    public int MaximumEdges { get; init; } = DefaultMaximumEdges;
    /// <summary>Gets the maximum caller-provided edge labels.</summary>
    public int MaximumLabels { get; init; } = DefaultMaximumLabels;

    /// <summary>Checks the positive work bounds and existing graph conformance ceilings.</summary>
    public bool IsValid() => MaximumDepth >= MinimumDepth && MaximumDepth <= MaximumConformingDepth
        && MaximumVertices >= MinimumWorkCount && MaximumVertices <= MaximumConformingVertices
        && MaximumEdges >= MinimumWorkCount && MaximumEdges <= MaximumConformingEdges
        && MaximumLabels >= MinimumWorkCount && MaximumLabels <= MaximumConformingLabels;

    /// <summary>Rejects invalid settings before database or traversal admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new InvalidOperationException(ValidationMessage); }
    }
}
