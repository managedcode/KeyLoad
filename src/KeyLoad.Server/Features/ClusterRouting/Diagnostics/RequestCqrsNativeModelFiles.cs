using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsNativeModelFiles(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, RequestCqrsProbeJson json,
    Lock sync, Func<RequestCqrsProbeSnapshot> readSnapshot)
{
    internal void Publish(RequestCqrsNativeModelWitness witness, RequestCqrsProbeMarkerRecord marker)
    {
        lock (sync)
        {
            if (witness.SessionId != sessionId) { throw Invalid(); }
            RequestCqrsNativeModelInventory.RequireMarkers([witness], [marker]);
            var snapshot = readSnapshot();
            records.ValidateMarker(marker, snapshot, null);
            var name = RequestCqrsNativeModelInventory.Name(witness.ArmId, witness.RequestId, witness.Phase);
            var bytes = RequestCqrsNativeModelInventory.Write(witness);
            if (bytes.Length > options.Value.MaximumRecordBytes) { throw Invalid(); }
            var markerBytes = json.WriteMarker(marker);
            var markerName = RequestCqrsProbeFiles.MarkerName(marker);
            RequestCqrsProbeBatchPublication.Write(root, [(name, bytes), (markerName, markerBytes)], options, readSnapshot);
            records.RegisterImmutable(name, bytes);
            records.RegisterImmutable(markerName, markerBytes);
        }
    }

    internal void Read(string path, string name, List<RequestCqrsNativeModelWitness> values,
        HashSet<string> presentControls)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = RequestCqrsNativeModelInventory.Read(bytes);
        if (value.SessionId != sessionId
            || RequestCqrsNativeModelInventory.Name(value.ArmId, value.RequestId, value.Phase) != name) { throw Invalid(); }
        records.RegisterImmutable(name, bytes);
        presentControls.Add(name);
        values.Add(value);
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
