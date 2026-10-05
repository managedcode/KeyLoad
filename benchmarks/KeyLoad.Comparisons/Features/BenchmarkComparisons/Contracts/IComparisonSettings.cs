namespace KeyLoad.Comparisons;

/// <summary>Bounded common numeric settings consumed by comparison target adapters.</summary>
public interface IComparisonSettings
{
    /// <summary>Gets the bounded documents setting.</summary>
    int Documents { get; }
    /// <summary>Gets the bounded operations setting.</summary>
    int Operations { get; }
    /// <summary>Gets the bounded warmup setting.</summary>
    int Warmup { get; }
    /// <summary>Gets the bounded repetitions setting.</summary>
    int Repetitions { get; }
    /// <summary>Gets the bounded concurrency setting.</summary>
    int Concurrency { get; }
    /// <summary>Gets the bounded payloadbytes setting.</summary>
    int PayloadBytes { get; }
    /// <summary>Gets the bounded seed setting.</summary>
    int Seed { get; }
    /// <summary>Gets the bounded dimensions setting.</summary>
    int Dimensions { get; }
    /// <summary>Gets the bounded topk setting.</summary>
    int TopK { get; }
    /// <summary>Gets the bounded timeoutseconds setting.</summary>
    int TimeoutSeconds { get; }
    /// <summary>Gets the bounded graphvertices setting.</summary>
    int GraphVertices { get; }
    /// <summary>Gets the bounded graphfanout setting.</summary>
    int GraphFanOut { get; }
    /// <summary>Gets the bounded graphdepth setting.</summary>
    int GraphDepth { get; }
}
