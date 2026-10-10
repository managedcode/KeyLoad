using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class NativeChecksumProfileEvidence
{
    internal static Task SaveAsync(string root, ZoneTreeStore store)
        => CommandIdempotencyCrashData.SaveEvidenceAsync(root, NativeChecksumProfileProtocol.CutFile, Capture(store));

    internal static async Task RequireAsync(string root, ZoneTreeStore store)
    {
        var original = await CommandIdempotencyCrashData.ReadEvidenceAsync<NativeChecksumProfileCut>(root, NativeChecksumProfileProtocol.CutFile);
        var current = Capture(store);
        if (current.NodeId != original.NodeId || current.Incarnation != original.Incarnation
            || current.Position != original.Position || current.ReadGeneration < original.ReadGeneration
            || !NativeSerialization.Serialize(current.Rows).AsSpan().SequenceEqual(NativeSerialization.Serialize(original.Rows)))
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static NativeChecksumProfileCut Capture(ZoneTreeStore store) => store.Read<NativeChecksumProfileCut>(view =>
    {
        var page = view.Scan([], CrashExecutionOptions.DatabaseLimits().Value.MaxScanRecords);
        if (page.HasMore || page.Records.Sum(row => row.Key.Length + (long)row.Value.Length)
            > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
        return new(store.Identity.NodeId, store.Identity.Incarnation, store.Position, store.Identity.ReadGeneration,
            [.. page.Records.Select(row => new NativeChecksumProfileRow(row.Key.ToArray(), row.Value.ToArray()))]);
    });
}
