using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Reads only already admitted immutable identities; unfamiliar arms cannot enter the inventory.</summary>
internal sealed class RequestCqrsProbeCleanupInventory(
    IReadOnlyDictionary<Guid, (byte[] Bytes, RequestCqrsProbeArmRecord Record)> arms,
    IReadOnlyCollection<Guid> retired, IReadOnlyDictionary<string, byte[]> controls, IOptions<RequestProbeExecutionOptions> options,
    RequestCqrsProbeJson json)
{
    internal void Read(string path, string name, List<RequestCqrsProbeLoadedArm> loaded,
        List<RequestCqrsProbeReleaseRecord> releases, List<RequestCqrsProbeMarkerRecord> markers,
        HashSet<string> present)
    {
        if (name.StartsWith(RequestCqrsProbeProtocol.ArmFilePrefix, StringComparison.Ordinal))
        {
            var known = arms.SingleOrDefault(pair => RequestCqrsProbeFiles.ArmName(pair.Value.Record) == name);
            if (known.Key == Guid.Empty)
            { return; }
            RequireBytes(path, known.Value.Bytes);
            loaded.Add(new(known.Value.Record, known.Value.Bytes));
            return;
        }
        if (name == RequestCqrsProbeProtocol.OwnerFile
            || name.StartsWith(RequestCqrsProbeProtocol.TemporaryFilePrefix, StringComparison.Ordinal))
        { return; }
        if (!controls.TryGetValue(name, out var bytes))
        { throw Invalid(); }
        RequireBytes(path, bytes);
        present.Add(name);
        if (name.StartsWith(RequestCqrsProbeProtocol.MarkerFilePrefix, StringComparison.Ordinal))
        { markers.Add(json.ReadMarker(bytes)); }
        else if (name.StartsWith(RequestCqrsProbeProtocol.ReleaseFilePrefix, StringComparison.Ordinal))
        { releases.Add(json.ReadRelease(bytes)); }
    }

    internal void RequireKnownArmPresence(IReadOnlyList<RequestCqrsProbeLoadedArm> loaded)
    {
        var present = loaded.Select(arm => arm.Record.ArmId).ToHashSet();
        if (present.Any(retired.Contains)
            || arms.Keys.Any(identity => !retired.Contains(identity) && !present.Contains(identity)))
        { throw Invalid(); }
    }

    internal void RequireArmQuota(IEnumerable<string> names)
    {
        var count = names.Where(name => name.StartsWith(RequestCqrsProbeProtocol.ArmFilePrefix, StringComparison.Ordinal))
            .Concat(arms.Values.Select(arm => RequestCqrsProbeFiles.ArmName(arm.Record)))
            .Concat(retired.Select(id => RequestCqrsProbeProtocol.ArmFilePrefix + id.ToString(RequestCqrsProbeProtocol.SessionIdFormat)
                + RequestCqrsProbeProtocol.JsonFileSuffix)).Distinct(StringComparer.Ordinal).Count();
        if (count > options.Value.MaximumArms)
        { throw Invalid(); }
    }

    private void RequireBytes(string path, byte[] expected)
    {
        var actual = RequestCqrsProbeFiles.ReadRecord(path, options);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
