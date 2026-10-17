using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsMovementJournalFiles(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, RequestCqrsProbeJson json,
    Lock sync, Func<RequestCqrsProbeSnapshot> readSnapshot)
{
    internal void Publish(RequestCqrsMovementJournalRecord value, RequestCqrsProbeMarkerRecord marker,
        RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            var snapshot = readSnapshot();
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
            RequestCqrsMovementJournalInventory.Require(value);
            RequestCqrsMovementJournalInventory.RequireArm(value, claim.Arm.Record);
            RequestCqrsMovementJournalInventory.RequireMarker(value, marker);
            if (value.SessionId != sessionId)
            {
                throw Invalid();
            }
            records.ValidateMarker(marker, snapshot, null);
            var name = RequestCqrsMovementJournalInventory.Name(value.ArmId, value.RequestId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, json.MovementJournalTypeInfo);
            if (bytes.Length > options.Value.MaximumRecordBytes)
            {
                throw Invalid();
            }
            var markerName = RequestCqrsProbeFiles.MarkerName(marker);
            var markerBytes = json.WriteMarker(marker);
            RequestCqrsProbeBatchPublication.Write(root, [(name, bytes), (markerName, markerBytes)],
                options, readSnapshot);
            records.RegisterImmutable(name, bytes);
            records.RegisterImmutable(markerName, markerBytes);
        }
    }

    internal void Read(string path, string name, List<RequestCqrsMovementJournalRecord> values,
        HashSet<string> presentControls)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = json.Read(bytes, RequestCqrsMovementJournalInventory.Fields, json.MovementJournalTypeInfo);
        RequestCqrsMovementJournalInventory.Require(value);
        if (value.SessionId != sessionId || RequestCqrsMovementJournalInventory.Name(value.ArmId, value.RequestId) != name)
        {
            throw Invalid();
        }
        records.RegisterImmutable(name, bytes);
        presentControls.Add(name);
        values.Add(value);
    }

    internal void RequireInventory(List<RequestCqrsMovementJournalRecord> values,
        List<RequestCqrsProbeMarkerRecord> markers, List<RequestCqrsProbeLoadedArm> arms)
    {
        RequestCqrsMovementJournalInventory.RequireInventory(values, markers, arms, records.RetainedArm);
        RequireArms(values, arms);
    }

    internal void RequireArms(IEnumerable<RequestCqrsMovementJournalRecord> values,
        IReadOnlyList<RequestCqrsProbeLoadedArm> arms)
    {
        foreach (var value in values)
        {
            var arm = arms.SingleOrDefault(candidate => candidate.Record.ArmId == value.ArmId)?.Record
                ?? records.RetainedArm(value.ArmId) ?? throw Invalid();
            RequestCqrsMovementJournalInventory.RequireArm(value, arm);
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
