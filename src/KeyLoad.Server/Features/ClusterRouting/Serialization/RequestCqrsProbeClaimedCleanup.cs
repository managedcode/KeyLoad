using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Producer disposal authenticates its retained claim without admitting unrelated invalid arm bytes.</summary>
internal sealed class RequestCqrsProbeClaimedCleanup(string root, byte[] ownerBytes,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeJson json,
    RequestCqrsProbeRecords records, Lock sync, Func<string, string, long> validateEntry)
{
    private const int OverflowEntry = 1;
    private const long InitialAggregateBytes = 0;
    internal void Write(RequestCqrsProbeMarkerRecord marker, RequestCqrsProbeLoadedArm claim)
    {
        lock (sync)
        {
            if (marker.Phase != RequestCqrsProbePhase.ProducerDisposed
                || marker.Outcome != RequestCqrsProbeOutcome.Observed || marker.ArmId != claim.Record.ArmId)
            { throw Invalid(); }
            var inventory = records.CreateCleanupInventory();
            var snapshot = Read(inventory);
            RequireClaim(claim, snapshot);
            records.ValidateMarker(marker, snapshot, claim);
            var name = RequestCqrsProbeFiles.MarkerName(marker);
            var bytes = json.WriteMarker(marker);
            RequestCqrsProbeAtomicFiles.Write(root, Path.Combine(root, name), bytes, options,
                () => Read(inventory));
            records.RegisterImmutable(name, bytes);
        }
    }

    private RequestCqrsProbeSnapshot Read(RequestCqrsProbeCleanupInventory inventory)
    {
        RequestCqrsProbePaths.RequireDirectory(root);
        var owner = RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, RequestCqrsProbeProtocol.OwnerFile), options);
        if (!CryptographicOperations.FixedTimeEquals(ownerBytes, owner))
        { throw Invalid(); }
        var entries = Directory.EnumerateFileSystemEntries(root).Take(options.Value.MaximumFiles + OverflowEntry).ToArray();
        if (entries.Length > options.Value.MaximumFiles)
        { throw Invalid(); }
        var arms = new List<RequestCqrsProbeLoadedArm>();
        var releases = new List<RequestCqrsProbeReleaseRecord>();
        var markers = new List<RequestCqrsProbeMarkerRecord>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        inventory.RequireArmQuota(entries.Select(EntryName));
        var total = InitialAggregateBytes;
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            total = checked(total + validateEntry(path, name));
            if (total > options.Value.MaximumAggregateBytes)
            { throw Invalid(); }
            inventory.Read(path, name, arms, releases, markers, present);
        }
        inventory.RequireKnownArmPresence(arms);
        records.ValidatePresence(present);
        records.ValidateInventory(markers);
        return new(arms, releases, markers, entries.Length, total);
    }

    private void RequireClaim(RequestCqrsProbeLoadedArm claim, RequestCqrsProbeSnapshot snapshot)
    {
        var current = snapshot.Arms.SingleOrDefault(arm => arm.Record.ArmId == claim.Record.ArmId);
        if (current is not null && CryptographicOperations.FixedTimeEquals(current.ExactBytes, claim.ExactBytes))
        { return; }
        if (current is null && records.IsRetiredArm(claim.Record.ArmId, claim.ExactBytes))
        { return; }
        throw Invalid();
    }

    private static string EntryName(string path) => Path.GetFileName(path);

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
