using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphVertexAuthorityFault
{
    private const string ForeignId = "foreign-canonical-vertex";
    private const string OriginalRequired = "The fixture must own its original canonical vertex row.";

    internal static GraphTraversalOwnedFault Install(ZoneTreeStore store, GraphTraversalIntegrityState state, bool start)
    {
        var selected = start ? state.DamagedEdge.From : state.DamagedEdge.To;
        var key = DocumentStorageKeys.RecordKey(selected);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(OriginalRequired);
        var record = OrleansSerialization.Deserialize<DocumentRecord>(original)
            ?? throw new InvalidOperationException(OriginalRequired);
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, record with
            {
                Reference = selected with
                {
                    Id = ForeignId
                }
            });
            return true;
        });
        return new(key, original);
    }

    internal static string OriginalRow(GraphTraversalOwnedFault fault)
        => Convert.ToHexString(fault.Key) + ":" + Convert.ToHexString(fault.Original);

    internal static string[] RepairedImage(string[] image, GraphTraversalOwnedFault fault)
    {
        var prefix = Convert.ToHexString(fault.Key) + ":";
        return image.Select(row => row.StartsWith(prefix, StringComparison.Ordinal) ? OriginalRow(fault) : row).ToArray();
    }
}
