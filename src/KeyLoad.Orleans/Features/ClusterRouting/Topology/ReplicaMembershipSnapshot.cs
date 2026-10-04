using System.Globalization;
using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipSnapshot
{
    private readonly ReplicaMembershipTableSnapshot snapshot;

    private ReplicaMembershipSnapshot(long expectedVersion, ReplicaMembershipTableSnapshot snapshot)
    {
        ExpectedVersion = expectedVersion;
        this.snapshot = snapshot;
    }

    internal long ExpectedVersion { get; }

    internal static ReplicaMembershipSnapshot Read(MembershipRecord? record)
    {
        var snapshot = record is null ? new ReplicaMembershipTableSnapshot(0, [])
            : NativeSerialization.Deserialize<ReplicaMembershipTableSnapshot>(record.Payload.Span);
        return new(record?.Version ?? 0, snapshot);
    }

    internal byte[] Serialize() => NativeSerialization.Serialize(snapshot);

    internal MembershipTableData Data(SiloAddress? address = null)
    {
        var rows = address is null ? snapshot.Rows : snapshot.Rows.Where(row => row.Address == address.ToParsableString()).ToArray();
        return new(rows.Select(row => Tuple.Create(row.ToEntry(), Version(row.ETag))).ToList(),
            new TableVersion(checked((int)snapshot.Version), Version(snapshot.Version)));
    }

    internal ReplicaMembershipSnapshot? Insert(MembershipEntry entry, TableVersion tableVersion)
    {
        if (!NextVersion(tableVersion) || snapshot.Rows.Any(row => row.Address == entry.SiloAddress.ToParsableString()))
        {
            return null;
        }

        return Next(new(tableVersion.Version, snapshot.Rows.Append(ReplicaMembershipRow.From(entry, 1)).ToArray()));
    }

    internal ReplicaMembershipSnapshot? Update(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        var previous = snapshot.Rows.FirstOrDefault(row => row.Address == entry.SiloAddress.ToParsableString());
        if (previous is null || etag != Version(previous.ETag) || !NextVersion(tableVersion))
        {
            return null;
        }

        // A status update based on an older read cannot move the heartbeat backwards.
        var updated = ReplicaMembershipRow.From(entry, checked(previous.ETag + 1)) with
        { Alive = entry.IAmAliveTime > previous.Alive ? entry.IAmAliveTime : previous.Alive };
        return Next(new(tableVersion.Version, snapshot.Rows.Select(row => row.Address == previous.Address ? updated : row).ToArray()));
    }

    internal ReplicaMembershipSnapshot? Heartbeat(MembershipEntry entry)
    {
        var address = entry.SiloAddress.ToParsableString();
        var previous = snapshot.Rows.FirstOrDefault(row => row.Address == address);
        if (previous is null || previous.Alive >= entry.IAmAliveTime)
        {
            return null;
        }

        // Heartbeats preserve both the table version and the row ETag.
        return Next(snapshot with
        { Rows = snapshot.Rows.Select(row => row.Address == address ? row with { Alive = entry.IAmAliveTime } : row).ToArray() });
    }

    internal ReplicaMembershipSnapshot Delete() => Next(new(checked(snapshot.Version + 1), []));

    internal ReplicaMembershipSnapshot? Cleanup(DateTimeOffset beforeDate)
    {
        var retained = snapshot.Rows.Where(row => row.Status != SiloStatus.Dead || row.Alive >= beforeDate.UtcDateTime).ToArray();
        return retained.Length == snapshot.Rows.Length ? null : Next(new(checked(snapshot.Version + 1), retained));
    }

    private bool NextVersion(TableVersion tableVersion) => snapshot.Version < long.MaxValue
        && tableVersion.Version == snapshot.Version + 1 && tableVersion.VersionEtag == Version(snapshot.Version);

    private ReplicaMembershipSnapshot Next(ReplicaMembershipTableSnapshot updated) => new(ExpectedVersion, updated);

    private static string Version(long value) => value.ToString(CultureInfo.InvariantCulture);
}
