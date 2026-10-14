using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsQueryReadCutFiles(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, RequestCqrsProbeJson json,
    Lock sync, Func<RequestCqrsProbeSnapshot> readSnapshot)
{
    internal void Publish(RequestCqrsQueryReadCutWitness value, RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            var snapshot = readSnapshot();
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
            RequestCqrsQueryReadCutInventory.Require(value);
            RequestCqrsQueryReadCutInventory.RequireArm(value, claim.Arm.Record);
            if (value.SessionId != sessionId)
            {
                throw Invalid();
            }
            RequestCqrsQueryReadCutInventory.RequireSource(value, snapshot.Markers);
            var marker = new RequestCqrsProbeMarkerRecord(RequestCqrsProbeProtocol.Version,
                RequestCqrsProbeProtocol.MarkerKind, value.SessionId, value.ArmId, value.RequestId,
                value.CommandId, RequestCqrsProbePhase.QueryReadCutCaptured, RequestCqrsProbeOutcome.Observed,
                value.Voter, value.SiloAddress, null, null);
            records.ValidateMarker(marker, snapshot, null);
            var name = RequestCqrsQueryReadCutInventory.Name(value.ArmId, value.RequestId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, json.QueryCutTypeInfo);
            if (bytes.Length > options.Value.MaximumRecordBytes)
            {
                throw Invalid();
            }
            var markerName = RequestCqrsProbeFiles.MarkerName(marker);
            RequestCqrsProbeBatchPublication.Write(root, [(name, bytes), (markerName, json.WriteMarker(marker))],
                options, readSnapshot);
            records.RegisterImmutable(name, bytes);
            records.RegisterImmutable(markerName, json.WriteMarker(marker));
        }
    }

    internal void Read(string path, string name, List<RequestCqrsQueryReadCutWitness> values,
        HashSet<string> presentControls)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = json.Read(bytes, RequestCqrsQueryReadCutInventory.Fields, json.QueryCutTypeInfo);
        RequestCqrsQueryReadCutInventory.Require(value);
        if (value.SessionId != sessionId || RequestCqrsQueryReadCutInventory.Name(value.ArmId, value.RequestId) != name)
        {
            throw Invalid();
        }
        records.RegisterImmutable(name, bytes);
        presentControls.Add(name);
        values.Add(value);
    }

    internal void RequireArms(IEnumerable<RequestCqrsQueryReadCutWitness> values,
        IReadOnlyList<RequestCqrsProbeLoadedArm> arms)
    {
        foreach (var value in values)
        {
            var arm = arms.SingleOrDefault(candidate => candidate.Record.ArmId == value.ArmId)?.Record
                ?? records.RetainedArm(value.ArmId) ?? throw Invalid();
            RequestCqrsQueryReadCutInventory.RequireArm(value, arm);
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
