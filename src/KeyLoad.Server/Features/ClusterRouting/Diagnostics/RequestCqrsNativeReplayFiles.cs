using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsNativeReplayFiles(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, RequestCqrsProbeJson json,
    Lock sync, Func<RequestCqrsProbeSnapshot> readSnapshot)
{
    private const int PublishedMarkerCount = 1;

    internal RequestCqrsNativeReplayPlan RequirePlan(RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, readSnapshot());
            var name = RequestCqrsNativeReplayInventory.PlanName(claim.Arm.Record.ArmId);
            var bytes = RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, name), options);
            var plan = RequestCqrsNativeReplayInventory.ReadPlan(bytes);
            RequestCqrsNativeReplayInventory.RequireArm(plan, claim.Arm.Record);
            records.RegisterImmutable(name, bytes);
            return plan;
        }
    }

    internal void Read(string path, string name, List<RequestCqrsNativeReplayPlan> plans,
        List<RequestCqrsNativeReplayWitness> witnesses, HashSet<string> present)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        if (RequestCqrsNativeReplayInventory.IsPlanName(name))
        {
            var plan = RequestCqrsNativeReplayInventory.ReadPlan(bytes);
            if (plan.SessionId != sessionId || name != RequestCqrsNativeReplayInventory.PlanName(plan.ArmId)) { throw Invalid(); }
            plans.Add(plan);
        }
        else
        {
            var value = RequestCqrsNativeReplayInventory.ReadWitness(bytes);
            if (value.SessionId != sessionId || name != RequestCqrsNativeReplayInventory.WitnessName(value.ArmId, value.RequestId, value.Ordinal)) { throw Invalid(); }
            witnesses.Add(value);
        }
        records.RegisterImmutable(name, bytes);
        present.Add(name);
    }

    internal void Publish(RequestCqrsProbeClaim claim, IReadOnlyList<RequestCqrsNativeReplayWitness> witnesses,
        RequestCqrsProbeMarkerRecord marker)
    {
        lock (sync)
        {
            var snapshot = readSnapshot();
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
            var plan = RequirePlan(claim);
            records.ValidateMarker(marker, snapshot, null);
            RequestCqrsNativeReplayInventory.RequireInventory([plan], witnesses, [claim.Arm], [marker], records.RetainedArm);
            var encoded = witnesses.Select(value => (Name: RequestCqrsNativeReplayInventory.WitnessName(value.ArmId, value.RequestId, value.Ordinal),
                Bytes: RequestCqrsNativeReplayInventory.WriteWitness(value))).ToArray();
            if (encoded.Any(item => item.Bytes.Length > options.Value.MaximumRecordBytes)) { throw Invalid(); }
            var markerBytes = json.WriteMarker(marker);
            if (checked(snapshot.FileCount + encoded.Length + PublishedMarkerCount) > options.Value.MaximumFiles
                || checked(snapshot.AggregateBytes + encoded.Sum(item => (long)item.Bytes.Length) + markerBytes.Length)
                    > options.Value.MaximumAggregateBytes) { throw Invalid(); }
            var markerName = RequestCqrsProbeFiles.MarkerName(marker);
            var publication = encoded.Append((Name: markerName, Bytes: markerBytes)).ToArray();
            RequestCqrsProbeBatchPublication.Write(root, publication, options, readSnapshot, plan);
            foreach (var item in publication) { records.RegisterImmutable(item.Name, item.Bytes); }
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
