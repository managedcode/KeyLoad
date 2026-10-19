namespace KeyLoad.Comparisons;

/// <summary>Execution vector and payload values read back from persisted database state.</summary>
/// <param name="Number">The persisted corpus number.</param>
/// <param name="Id">The persisted record ID.</param>
/// <param name="Dimensions">The persisted vector dimension.</param>
/// <param name="VectorSha256">SHA256 over ordered little-endian float32 bits.</param>
/// <param name="PayloadSha256">SHA256 over persisted UTF8 payload bytes.</param>
public sealed record VectorReadback(int Number, string Id, int Dimensions, string VectorSha256, string PayloadSha256);
