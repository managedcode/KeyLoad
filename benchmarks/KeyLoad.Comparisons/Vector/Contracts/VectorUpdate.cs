namespace KeyLoad.Comparisons;

/// <summary>A deterministic embedding update in the mixed profile.</summary>
/// <param name="Number">The corpus number being updated.</param>
/// <param name="Id">The canonical record ID.</param>
/// <param name="Embedding">The replacement embedding.</param>
public sealed record VectorUpdate(int Number, string Id, ReadOnlyMemory<float> Embedding);
