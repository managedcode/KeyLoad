using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeActivationInventory(string sessionId, RequestCqrsProbeJson json,
    RequestCqrsProbeRecords records, IOptions<RequestProbeExecutionOptions> options)
{
    internal void Read(string path, string name, List<RequestCqrsProbeActivationRecord> witnesses, HashSet<string> present)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = json.ReadActivation(bytes);
        if (value.SessionId != sessionId || RequestCqrsProbeActivationValidation.Name(value) != name)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
        records.RegisterImmutable(name, bytes);
        present.Add(name);
        witnesses.Add(value);
    }

    internal static void RequireMarkers(List<RequestCqrsProbeActivationRecord> witnesses,
        List<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var value in witnesses)
        { RequestCqrsProbeActivationValidation.RequireMarker(value, markers); }
    }
}
