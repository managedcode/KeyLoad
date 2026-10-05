namespace KeyLoad.Comparisons;

/// <summary>Provides deterministic bounded-index access to a comparison corpus.</summary>
public interface IComparisonCorpus
{
    /// <summary>Gets the validated common settings for this corpus.</summary>
    IComparisonSettings Settings { get; }
    /// <summary>Gets deterministic indexed documents without requiring a retained corpus array.</summary>
    IReadOnlyList<BenchmarkDocument> Documents { get; }
    /// <summary>Gets the directed graph edges required by the selected corpus.</summary>
    IReadOnlyList<BenchmarkEdge> Edges { get; }
    /// <summary>Gets the number of graph vertices.</summary>
    int GraphVertexCount { get; }
    /// <summary>Gets the ordered canonical corpus digest.</summary>
    string Sha256 { get; }
    /// <summary>Creates one deterministic document by numeric identity.</summary>
    BenchmarkDocument CreateDocument(int number);
    /// <summary>Selects one operation input without retaining an input table.</summary>
    BenchmarkDocument Input(Scenario scenario, int repetition, int operation, bool warmup);
}
