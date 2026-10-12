using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeBatchPublication
{
    private const int MinimumPublicationFiles = 2;
    private const int MaximumOrdinaryPublicationFiles = 3;
    private const int AllFaultPublicationFiles = 7;
    private const int MarkerFileCount = 1;
    private const int EmptyRecordBytes = 0;
    private const string TemporarySuffix = ".tmp";
    // Caller holds the existing owner lock for this entire publication.
    internal static void Write(string root, IReadOnlyList<(string Name, byte[] Bytes)> entries,
        IOptions<RequestProbeExecutionOptions> options, Func<RequestCqrsProbeSnapshot> readSnapshot,
        RequestCqrsNativeReplayPlan? replayPlan = null)
    {
        if (OperatingSystem.IsWindows() || entries.Count < MinimumPublicationFiles
            || entries.Count > MaximumOrdinaryPublicationFiles && (entries.Count != AllFaultPublicationFiles || replayPlan?.ReplayMode != RemoteModelNativeReplayVariation.AllWireFaults6)
            || entries.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        { throw Invalid(); }
        var before = readSnapshot();
        if (replayPlan is not null)
        {
            if (entries.Count != RemoteModelNativeReplayCatalog.Count(replayPlan.ReplayMode) + MarkerFileCount)
            { throw Invalid(); }
            var witnesses = entries.Take(entries.Count - MarkerFileCount).Select(item => RequestCqrsNativeReplayInventory.ReadWitness(item.Bytes)).ToArray();
            var marker = new RequestCqrsProbeJson(options).ReadMarker(entries[^MarkerFileCount].Bytes);
            RequestCqrsNativeReplayInventory.RequireInventory([replayPlan], witnesses, before.Arms, [marker], _ => null);
            if (entries[^MarkerFileCount].Name != RequestCqrsProbeFiles.MarkerName(marker)
                || entries.Take(entries.Count - MarkerFileCount).Where((item, index) => item.Name != RequestCqrsNativeReplayInventory.WitnessName(
                    witnesses[index].ArmId, witnesses[index].RequestId, witnesses[index].Ordinal)).Any())
            { throw Invalid(); }
        }
        if (checked(before.FileCount + entries.Count) > options.Value.MaximumFiles
            || checked(before.AggregateBytes + entries.Sum(item => (long)item.Bytes.Length)) > options.Value.MaximumAggregateBytes)
        { throw Invalid(); }
        var staged = new List<(string Temporary, string Destination)>();
        foreach (var entry in entries)
        {
            if (entry.Bytes.Length is <= EmptyRecordBytes || entry.Bytes.Length > options.Value.MaximumRecordBytes
                || !RequestCqrsProbeKnownNames.KnownName(entry.Name) || Path.GetFileName(entry.Name) != entry.Name)
            { throw Invalid(); }
            var destination = Path.Combine(root, entry.Name);
            if (File.Exists(destination))
            { throw Invalid(); }
            var temporary = Path.Combine(root, RequestCqrsProbeProtocol.TemporaryFilePrefix + Guid.NewGuid().ToString(RequestCqrsProbeProtocol.SessionIdFormat) + TemporarySuffix);
            using (var stream = new FileStream(temporary, new FileStreamOptions
            {
                BufferSize = options.Value.FileBufferBytes,
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
                Options = FileOptions.WriteThrough
            }))
            { stream.Write(entry.Bytes); stream.Flush(flushToDisk: true); }
            staged.Add((temporary, destination));
        }
        // All staged files are included in the original strict inventory and quota validation.
        _ = readSnapshot();
        foreach (var item in staged)
        { File.Move(item.Temporary, item.Destination, overwrite: false); }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
