namespace KeyLoad.Comparisons;

/// <summary>Holds trusted isolated worker selectors and detects forbidden immutable-workload overrides.</summary>
[ConfigurationOptions]
public sealed class ComparisonWorkerSelectionOptions
{
    /// <summary>Names the workflow selector section.</summary>
    public const string SectionName = "Benchmarks";

    /// <summary>Gets or sets the canonical engine name.</summary>
    public string? Target { get; set; }

    /// <summary>Gets or sets the actual native member count.</summary>
    public string? NodeCount { get; set; }

    /// <summary>Gets or sets the exact workload scenario.</summary>
    public string? Scenario { get; set; }

    /// <summary>Gets or sets the authenticated profile identifier.</summary>
    public string? EvidenceProfile { get; set; }

    /// <summary>Gets or sets the optional immutable scale profile.</summary>
    public string? ScaleProfile { get; set; }

    /// <summary>Gets or sets the optional immutable vector profile.</summary>
    public string? VectorProfile { get; set; }

    /// <summary>Gets or sets the optional offered-rate cohort.</summary>
    public string? OpenLoopRate { get; set; }

    /// <summary>Gets or sets the optional AppHost mode.</summary>
    public string? Profile { get; set; }

    /// <summary>Gets or sets the document count override.</summary>
    public string? Documents { get; set; }

    /// <summary>Gets or sets the operation count override.</summary>
    public string? Operations { get; set; }

    /// <summary>Gets or sets the warmup override.</summary>
    public string? Warmup { get; set; }

    /// <summary>Gets or sets the repetition override.</summary>
    public string? Repetitions { get; set; }

    /// <summary>Gets or sets the concurrency override.</summary>
    public string? Concurrency { get; set; }

    /// <summary>Gets or sets the payload size override.</summary>
    public string? PayloadBytes { get; set; }

    /// <summary>Gets or sets the seed override.</summary>
    public string? Seed { get; set; }

    /// <summary>Gets or sets the vector dimension override.</summary>
    public string? Dimensions { get; set; }

    /// <summary>Gets or sets the neighbor count override.</summary>
    public string? TopK { get; set; }

    /// <summary>Gets or sets the operation timeout override.</summary>
    public string? TimeoutSeconds { get; set; }

    /// <summary>Gets or sets the graph vertex override.</summary>
    public string? GraphVertices { get; set; }

    /// <summary>Gets or sets the graph fan-out override.</summary>
    public string? GraphFanOut { get; set; }

    /// <summary>Gets or sets the graph depth override.</summary>
    public string? GraphDepth { get; set; }
}
