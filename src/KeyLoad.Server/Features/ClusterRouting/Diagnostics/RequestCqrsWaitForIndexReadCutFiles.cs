using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsWaitForIndexReadCutFiles(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, RequestCqrsProbeJson json,
    Lock sync, Func<RequestCqrsProbeSnapshot> readSnapshot)
{
    internal void Publish(RequestCqrsWaitForIndexReadCutWitness value, RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            var snapshot = readSnapshot();
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
            RequestCqrsWaitForIndexReadCutInventory.Require(value);
            RequestCqrsWaitForIndexReadCutInventory.RequireArm(value, claim.Arm.Record);
            if (value.SessionId != sessionId)
            { throw Invalid(); }
            RequestCqrsWaitForIndexReadCutInventory.RequireSource(value, snapshot.Markers);
            var marker = new RequestCqrsProbeMarkerRecord(RequestCqrsProbeProtocol.Version,
                RequestCqrsProbeProtocol.MarkerKind, value.SessionId, value.ArmId, value.RequestId,
                value.CommandId, RequestCqrsProbePhase.WaitForIndexReadCutCaptured, RequestCqrsProbeOutcome.Observed,
                value.Voter, value.SiloAddress, null, null);
            records.ValidateMarker(marker, snapshot, null);
            var name = RequestCqrsWaitForIndexReadCutInventory.Name(value.ArmId, value.RequestId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, json.WaitCutTypeInfo);
            if (bytes.Length > options.Value.MaximumRecordBytes)
            { throw Invalid(); }
            var markerName = RequestCqrsProbeFiles.MarkerName(marker);
            RequestCqrsProbeBatchPublication.Write(root, [(name, bytes), (markerName, json.WriteMarker(marker))],
                options, readSnapshot);
            records.RegisterImmutable(name, bytes);
            records.RegisterImmutable(markerName, json.WriteMarker(marker));
        }
    }

    internal void Read(string path, string name, List<RequestCqrsWaitForIndexReadCutWitness> values,
        HashSet<string> presentControls)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = json.Read(bytes, RequestCqrsWaitForIndexReadCutInventory.Fields, json.WaitCutTypeInfo);
        RequestCqrsWaitForIndexReadCutInventory.Require(value);
        if (value.SessionId != sessionId || RequestCqrsWaitForIndexReadCutInventory.Name(value.ArmId, value.RequestId) != name)
        { throw Invalid(); }
        records.RegisterImmutable(name, bytes);
        presentControls.Add(name);
        values.Add(value);
    }

    internal void RequireArms(IEnumerable<RequestCqrsWaitForIndexReadCutWitness> values,
        IReadOnlyList<RequestCqrsProbeLoadedArm> arms)
    {
        foreach (var value in values)
        {
            var arm = arms.SingleOrDefault(candidate => candidate.Record.ArmId == value.ArmId)?.Record
                ?? records.RetainedArm(value.ArmId) ?? throw Invalid();
            RequestCqrsWaitForIndexReadCutInventory.RequireArm(value, arm);
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
