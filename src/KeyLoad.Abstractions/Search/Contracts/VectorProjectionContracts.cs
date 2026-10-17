namespace KeyLoad;

/// <summary>Defines stable native identities for guarded vector projection contracts.</summary>
internal static class VectorProjectionContractAliases
{
    internal const string Apply = "keyload.contract.apply-vector-projection.v1";
}

/// <summary>Atomically applies a vector derived from one current document field and exact event input.</summary>
/// <param name="SourceStream">The exact source stream generation.</param>
/// <param name="SourceEventRevision">The retained event revision that caused this projection.</param>
/// <param name="SourceEventId">The event identity at that stream revision.</param>
/// <param name="SourceDocument">The canonical source entity within the same partition.</param>
/// <param name="SourceDocumentRevision">The exact source document revision used by the reducer.</param>
/// <param name="InputField">The source field whose persisted use policy was authorized.</param>
/// <param name="ReducerId">The stable reducer identity.</param>
/// <param name="ReducerVersion">The stable reducer code/version identity.</param>
/// <param name="ReducerGeneration">The explicit reducer or projection generation.</param>
/// <param name="Target">The canonical vector mutation and independent target revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(VectorProjectionContractAliases.Apply)]
public sealed record ApplyVectorProjection(
    [property: Orleans.Id(0)] StreamRef SourceStream,
    [property: Orleans.Id(1)] long SourceEventRevision,
    [property: Orleans.Id(2)] string SourceEventId,
    [property: Orleans.Id(3)] EntityRef SourceDocument,
    [property: Orleans.Id(4)] long SourceDocumentRevision,
    [property: Orleans.Id(5)] string InputField,
    [property: Orleans.Id(6)] string ReducerId,
    [property: Orleans.Id(7)] string ReducerVersion,
    [property: Orleans.Id(8)] long ReducerGeneration,
    [property: Orleans.Id(9)] PutVector Target) : Mutation(Target.Collection);
