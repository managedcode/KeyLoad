using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsEventVectorExpiryFiles(string root, RequestCqrsProbeJson json,
    RequestCqrsProbeRecords records, IOptions<RequestProbeExecutionOptions> options, Lock sync,
    Func<RequestCqrsProbeSnapshot> snapshot, Action<string, byte[]> write)
{
    internal void Read(string path, string name, List<RequestCqrsEventVectorExpiryRecord> values, HashSet<string> present)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = RequestCqrsEventVectorExpiryJson.Read(json, bytes);
        if (RequestCqrsEventVectorExpiryJson.Name(value) != name)
        { throw RequestCqrsEventVectorExpiryJson.Invalid(); }
        records.RegisterImmutable(name, bytes);
        present.Add(name);
        values.Add(value);
    }

    internal void Publish(RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot());
            if (claim.Arm.Record.Phase != RequestCqrsProbePhase.EventVectorFirstDispatchReserved
                || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold
                || claim.Arm.Record.CommandId != claim.Identity.CommandId)
            { throw RequestCqrsEventVectorExpiryJson.Invalid(); }
            var value = new RequestCqrsEventVectorExpiryRecord(RequestCqrsProbeProtocol.Version,
                claim.Identity.RequestId, claim.Identity.CommandId, claim.Identity.OriginalExpiresAt);
            var name = RequestCqrsEventVectorExpiryJson.Name(value);
            var bytes = RequestCqrsEventVectorExpiryJson.Write(json, value);
            write(Path.Combine(root, name), bytes);
            records.RegisterImmutable(name, bytes);
        }
    }

    internal static void RequireInventory(List<RequestCqrsEventVectorExpiryRecord> values,
        List<RequestCqrsProbeLoadedArm> arms, List<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var value in values)
        {
            if (!markers.Any(marker => marker.RequestId == value.RequestId && marker.CommandId == value.CommandId
                && marker.Phase == RequestCqrsProbePhase.EventVectorFirstDispatchReserved
                && marker.Outcome == RequestCqrsProbeOutcome.Observed)
                && !arms.Any(arm => arm.Record.CommandId == value.CommandId
                    && arm.Record.Phase == RequestCqrsProbePhase.EventVectorFirstDispatchReserved
                    && arm.Record.Action == RequestCqrsProbeAction.Hold))
            { throw RequestCqrsEventVectorExpiryJson.Invalid(); }
        }
    }
}
