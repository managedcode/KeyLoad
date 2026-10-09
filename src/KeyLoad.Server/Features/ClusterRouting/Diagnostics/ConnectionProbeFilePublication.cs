using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ConnectionProbeFilePublication(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, Lock sync,
    Func<RequestCqrsProbeSnapshot> readSnapshot, Action<string, byte[]> writeAtomic)
{
    internal void Write(ConnectionProbeWitness value)
    {
        lock (sync)
        {
            var snapshot = readSnapshot();
            ConnectionProbeWitness[] witnesses = value.Closed
                ? [ConnectionProbeInventory.Read(RequestCqrsProbeFiles.ReadRecord(Path.Combine(root,
                    ConnectionProbeInventory.Name(value.ArmId, value.RequestId, closed: false)), options)), value]
                : [value];
            ConnectionProbeInventory.RequireMarkers(witnesses, snapshot.Markers);
            var name = ConnectionProbeInventory.Name(value);
            var bytes = ConnectionProbeInventory.Write(value);
            writeAtomic(Path.Combine(root, name), bytes);
            records.RegisterImmutable(name, bytes);
        }
    }

    internal void Read(string path, string name, List<ConnectionProbeWitness> witnesses,
        HashSet<string> presentControls)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = ConnectionProbeInventory.Read(bytes);
        if (value.SessionId != sessionId || ConnectionProbeInventory.Name(value) != name)
        { throw new InvalidOperationException(ConnectionProbeProtocol.Invalid); }
        records.RegisterImmutable(name, bytes);
        presentControls.Add(name);
        witnesses.Add(value);
    }
}
