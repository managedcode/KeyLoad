using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipSnapshot
{
    private readonly TableSnapshot snapshot;

    private ReplicaMembershipSnapshot(long expectedVersion, TableSnapshot snapshot)
    {
        ExpectedVersion = expectedVersion;
        this.snapshot = snapshot;
    }

    internal long ExpectedVersion { get; }

    internal static ReplicaMembershipSnapshot Read(MembershipRecord? record)
    {
        var snapshot = record is null ? new TableSnapshot(0, [])
            : JsonDefaults.Deserialize<TableSnapshot>(Encoding.UTF8.GetBytes(record.Json));
        return new(record?.Version ?? 0, snapshot);
    }

    internal string Serialize() => JsonSerializer.Serialize(snapshot, JsonDefaults.Options);

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

        return Next(new(tableVersion.Version, snapshot.Rows.Append(Row.From(entry, 1)).ToArray()));
    }

    internal ReplicaMembershipSnapshot? Update(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        var previous = snapshot.Rows.FirstOrDefault(row => row.Address == entry.SiloAddress.ToParsableString());
        if (previous is null || etag != Version(previous.ETag) || !NextVersion(tableVersion))
        {
            return null;
        }

        // A status update based on an older read cannot move the heartbeat backwards.
        var updated = Row.From(entry, checked(previous.ETag + 1)) with
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

    private ReplicaMembershipSnapshot Next(TableSnapshot updated) => new(ExpectedVersion, updated);

    private static string Version(long value) => value.ToString(CultureInfo.InvariantCulture);

    // These private DTO names and constructor order retain the existing persisted membership JSON schema.
    private sealed record TableSnapshot(long Version, Row[] Rows);
    private sealed record Suspect(string Address, DateTime Time);
    private sealed record Row(string Address, SiloStatus Status, int ProxyPort, string Host, string Name,
        DateTime Started, DateTime Alive, Suspect[] Suspects, long ETag)
    {
        /// <summary>Copies all native membership fields into the unchanged persisted row format.</summary>
        public static Row From(MembershipEntry entry, long etag) => new(entry.SiloAddress.ToParsableString(), entry.Status,
            entry.ProxyPort, entry.HostName, entry.SiloName, entry.StartTime, entry.IAmAliveTime,
            (entry.SuspectTimes ?? []).Select(pair => new Suspect(pair.Item1.ToParsableString(), pair.Item2)).ToArray(), etag);

        /// <summary>Reconstructs the native row without dropping suspicion or heartbeat metadata.</summary>
        public MembershipEntry ToEntry() => new()
        {
            SiloAddress = SiloAddress.FromParsableString(Address),
            Status = Status,
            ProxyPort = ProxyPort,
            HostName = Host,
            SiloName = Name,
            StartTime = Started,
            IAmAliveTime = Alive,
            SuspectTimes = Suspects.Select(suspect => Tuple.Create(SiloAddress.FromParsableString(suspect.Address), suspect.Time)).ToList()
        };
    }
}
