using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Continues a query ordered by ascending document id after the last id of the previous page.</summary>
/// <remarks>Document ids order exactly as the collection's storage keys within one partition, so a full scan seeks to
/// the key after <see cref="AfterId"/> and stops once one row more than the page holds is accepted; no page reads the
/// rows of earlier pages again. Other access paths skip the rows at or before <see cref="AfterId"/>.</remarks>
/// <param name="AfterId">The last document id of the previous page, or null for the first page.</param>
/// <param name="Satisfied">Reports whether the page and the row that proves another page have been accepted.</param>
internal sealed record QueryKeysetPage(string? AfterId, Func<bool> Satisfied)
{
    private const int EqualOrder = 0;
    private readonly byte[]? afterKey = AfterId is null ? null : KeyCodec.Encode(AfterId);

    /// <summary>Whether a query pages by document id instead of by offset.</summary>
    internal static bool Applies(SelectQuery query)
        => query is { Explain: false, Offset: 0, Order: [{ Path: SqlSyntax.MetadataIdPath, Descending: false }] };

    /// <summary>Whether a document lies after the previous page.</summary>
    internal bool Follows(DocumentRecord document)
        => afterKey is null || BinaryKeyComparer.Instance.Compare(KeyCodec.Encode(document.Reference.Id), afterKey) > EqualOrder;
}
