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

    internal static ReplicaMembershipSnapshot Read(MembershipRecord? record, int maximumRows = 0)
    {
        var snapshot = record is null ? new ReplicaMembershipTableSnapshot(0, [])
            : NativeSerialization.Deserialize<ReplicaMembershipTableSnapshot>(record.Payload.Span);
        var result = new ReplicaMembershipSnapshot(record?.Version ?? 0, snapshot);
        result.ValidateCapacity(maximumRows);
        return result;
    }

    internal byte[] Serialize() => NativeSerialization.Serialize(snapshot);
    internal int RowCount => snapshot.Rows.Length;

    internal MembershipTableData Data(SiloAddress? address = null)
    {
        var rows = address is null ? snapshot.Rows : snapshot.Rows.Where(row => row.Address == address.ToParsableString()).ToArray();
        return new(rows.Select(row => Tuple.Create(row.ToEntry(), Version(row.ETag))).ToList(),
            new TableVersion(checked((int)snapshot.Version), Version(snapshot.Version)));
    }

    internal ReplicaMembershipSnapshot? Insert(MembershipEntry entry, TableVersion tableVersion, int maximumRows = 0)
    {
        if (!NextVersion(tableVersion) || snapshot.Rows.Any(row => row.Address == entry.SiloAddress.ToParsableString()))
        {
            return null;
        }
        CheckCanAdd(entry, maximumRows);
        var next = Next(new(tableVersion.Version, snapshot.Rows.Append(ReplicaMembershipRow.From(entry, 1)).ToArray()));
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal ReplicaMembershipSnapshot? Update(MembershipEntry entry, string etag, TableVersion tableVersion, int maximumRows = 0)
    {
        var previous = snapshot.Rows.FirstOrDefault(row => row.Address == entry.SiloAddress.ToParsableString());
        if (previous is null || etag != Version(previous.ETag) || !NextVersion(tableVersion))
        {
            return null;
        }

        // A status update based on an older read cannot move the heartbeat backwards.
        var updated = ReplicaMembershipRow.From(entry, checked(previous.ETag + 1)) with
        { Alive = entry.IAmAliveTime > previous.Alive ? entry.IAmAliveTime : previous.Alive };
        var next = Next(new(tableVersion.Version, snapshot.Rows.Select(row => row.Address == previous.Address ? updated : row).ToArray()));
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal ReplicaMembershipSnapshot? Heartbeat(MembershipEntry entry, int maximumRows = 0)
    {
        var address = entry.SiloAddress.ToParsableString();
        var previous = snapshot.Rows.FirstOrDefault(row => row.Address == address);
        if (previous is null || previous.Alive >= entry.IAmAliveTime)
        {
            return null;
        }

        // Heartbeats preserve both the table version and the row ETag.
        var next = Next(snapshot with
        { Rows = snapshot.Rows.Select(row => row.Address == address ? row with { Alive = entry.IAmAliveTime } : row).ToArray() });
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal ReplicaMembershipSnapshot Delete() => Next(new(checked(snapshot.Version + 1), []));

    internal ReplicaMembershipSnapshot? Cleanup(DateTimeOffset beforeDate, int maximumRows = 0)
    {
        var retained = snapshot.Rows.Where(row => row.Status != SiloStatus.Dead || row.Alive >= beforeDate.UtcDateTime).ToArray();
        if (retained.Length == snapshot.Rows.Length)
        { return null; }
        var next = Next(new(checked(snapshot.Version + 1), retained));
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal void ValidateCapacity(int maximumRows)
    {
        if (maximumRows <= 0)
        { return; }
        if (snapshot.Rows.Length > maximumRows || Serialize().Length > ReplicaMembershipAuthorityProtocol.MaximumSnapshotBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        foreach (var row in snapshot.Rows)
        {
            if (NativeSerialization.Measure(row) > ReplicaMembershipAuthorityProtocol.MaximumRowBytes
                || row.Suspects.Length > ReplicaMembershipAuthorityProtocol.MaximumSuspects
                || !ReplicaMembershipAuthorityValidation.Bounded(row.Address) || !ReplicaMembershipAuthorityValidation.Bounded(row.Host)
                || !ReplicaMembershipAuthorityValidation.Bounded(row.Name)
                || row.Suspects.Any(suspect => !ReplicaMembershipAuthorityValidation.Bounded(suspect.Address)))
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        }
    }

    private void CheckCanAdd(MembershipEntry entry, int maximumRows)
    {
        if (maximumRows > 0 && snapshot.Rows.Length >= maximumRows)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        if (maximumRows > 0)
        { ReplicaMembershipAuthorityValidation.Entry(ReplicaMembershipAuthorityMapping.ToWire(entry, "0")); }
    }

    private bool NextVersion(TableVersion tableVersion) => snapshot.Version < long.MaxValue
        && tableVersion.Version == snapshot.Version + 1 && tableVersion.VersionEtag == Version(snapshot.Version);

    private ReplicaMembershipSnapshot Next(ReplicaMembershipTableSnapshot updated) => new(ExpectedVersion, updated);

    private static string Version(long value) => value.ToString(CultureInfo.InvariantCulture);
}
