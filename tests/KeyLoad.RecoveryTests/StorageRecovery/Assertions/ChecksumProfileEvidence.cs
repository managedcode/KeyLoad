using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ChecksumProfileEvidence
{
    internal static Task SaveAsync(string root, ZoneTreeStore store)
        => CommandIdempotencyCrashData.SaveEvidenceAsync(root, ChecksumProfileProtocol.CutFile, Capture(store));

    internal static async Task RequireAsync(string root, ZoneTreeStore store)
    {
        var original = await CommandIdempotencyCrashData.ReadEvidenceAsync<ChecksumProfileCut>(root, ChecksumProfileProtocol.CutFile);
        var current = Capture(store);
        if (current.NodeId != original.NodeId || current.Incarnation != original.Incarnation
            || current.Position != original.Position || current.ReadGeneration < original.ReadGeneration
            || !OrleansSerialization.Serialize(current.Rows).AsSpan().SequenceEqual(OrleansSerialization.Serialize(original.Rows)))
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
    }

    private static ChecksumProfileCut Capture(ZoneTreeStore store) => store.Read<ChecksumProfileCut>(view =>
    {
        var page = view.Scan([], CrashExecutionOptions.DatabaseLimits().Value.MaxScanRecords);
        if (page.HasMore || page.Records.Sum(row => row.Key.Length + (long)row.Value.Length)
            > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
        return new(store.Identity.NodeId, store.Identity.Incarnation, store.Position, store.Identity.ReadGeneration,
            [.. page.Records.Select(row => new ChecksumProfileRow(row.Key.ToArray(), row.Value.ToArray()))]);
    });
}
