namespace KeyLoad.Comparisons;

/// <summary>A lazily generated deterministic vector document for execution ingestion.</summary>
/// <param name="Number">The deterministic corpus number.</param>
/// <param name="Id">The canonical ordinal record ID.</param>
/// <param name="Embedding">The owned float32 vector.</param>
/// <param name="Payload">The exact UTF8 payload.</param>
public sealed record VectorDocument(int Number, string Id, ReadOnlyMemory<float> Embedding, string Payload);
