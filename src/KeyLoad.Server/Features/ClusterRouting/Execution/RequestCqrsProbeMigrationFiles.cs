using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Borrows the original private file owner and immutable bounded inventory.</summary>
internal sealed class RequestCqrsProbeMigrationFiles(string root, string session, RequestCqrsProbeMigrationJson json,
    RequestCqrsProbeRecords records, IOptions<RequestProbeExecutionOptions> options, Lock sync,
    Func<RequestCqrsProbeSnapshot> snapshot, Action<string, byte[]> write)
{
    private RequestCqrsProbeMigrationRecord[] current = [];

    internal void Read(string path, string name, List<RequestCqrsProbeMigrationRecord> values, HashSet<string> present)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = json.Read(bytes);
        if (value.SessionId != session || RequestCqrsProbeMigrationValidation.Name(value) != name)
        { throw Invalid(); }
        records.RegisterImmutable(name, bytes);
        present.Add(name);
        values.Add(value);
    }

    internal RequestCqrsProbeMigrationRecord? Find(RequestCqrsProbeActivationRecord witness)
    {
        lock (sync)
        {
            _ = snapshot();
            var requests = current.Where(value => value.Kind == RequestCqrsProbeMigrationProtocol.RequestKind
                    && RequestCqrsProbeMigrationValidation.Witness(value) == witness).ToArray();
            if (requests.Length > RequestCqrsProbeMigrationProtocol.SingleRequest)
            { throw Invalid(); }
            return requests.Length == RequestCqrsProbeMigrationProtocol.SingleRequest
                ? requests[RequestCqrsProbeMigrationProtocol.FirstRequest] : null;
        }
    }

    internal void Requested(RequestCqrsProbeMigrationRecord request)
    {
        lock (sync)
        {
            _ = snapshot();
            var value = request with { Kind = RequestCqrsProbeMigrationProtocol.RequestedKind };
            var name = RequestCqrsProbeMigrationValidation.Name(value);
            var bytes = json.Write(value);
            write(Path.Combine(root, name), bytes);
            records.RegisterImmutable(name, bytes);
        }
    }

    internal void CommitInventory(List<RequestCqrsProbeMigrationRecord> values) => current = [.. values];

    internal static void RequireInventory(List<RequestCqrsProbeMigrationRecord> values,
        List<RequestCqrsProbeActivationRecord> witnesses, List<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var value in values)
        {
            var witness = RequestCqrsProbeMigrationValidation.Witness(value);
            if (!witnesses.Contains(witness))
            { throw Invalid(); }
            RequestCqrsProbeActivationValidation.RequireMarker(witness, markers);
            if (value.Kind == RequestCqrsProbeMigrationProtocol.RequestedKind
                && !values.Contains(value with { Kind = RequestCqrsProbeMigrationProtocol.RequestKind }))
            { throw Invalid(); }
        }
    }
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
