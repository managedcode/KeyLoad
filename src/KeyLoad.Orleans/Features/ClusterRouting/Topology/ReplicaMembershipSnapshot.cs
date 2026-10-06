using Microsoft.Extensions.Options;
using System.Globalization;
using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipSnapshot
{
    private const int DeleteVersionStep = 1;
    private const int NextVersionVersionStep = 1;

    private readonly ReplicaMembershipTableSnapshot snapshot;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;

    private ReplicaMembershipSnapshot(long expectedVersion, ReplicaMembershipTableSnapshot snapshot, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        ExpectedVersion = expectedVersion;
        this.snapshot = snapshot;
        this.membershipOptions = membershipOptions;
    }

    internal long ExpectedVersion { get; }

    internal static ReplicaMembershipSnapshot Read(MembershipRecord? record, IOptions<OrleansMembershipOptions> membershipOptions, int maximumRows = ReplicaMembershipProtocol.UnboundedRows)
    {
        const int VersionEmptyCount = 0;
        const int RecordVersionValidationBoundary = 0;

        var snapshot = record is null ? new ReplicaMembershipTableSnapshot(VersionEmptyCount, [])
            : NativeSerialization.Deserialize<ReplicaMembershipTableSnapshot>(record.Payload.Span);
        var result = new ReplicaMembershipSnapshot(record?.Version ?? RecordVersionValidationBoundary, snapshot, membershipOptions);
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

    internal ReplicaMembershipSnapshot? Insert(MembershipEntry entry, TableVersion tableVersion, int maximumRows = ReplicaMembershipProtocol.UnboundedRows)
    {
        const int EtagSingleItemCount = 1;

        if (!NextVersion(tableVersion) || snapshot.Rows.Any(row => row.Address == entry.SiloAddress.ToParsableString()))
        {
            return null;
        }
        CheckCanAdd(entry, maximumRows);
        var next = Next(new(tableVersion.Version, snapshot.Rows.Append(ReplicaMembershipRow.From(entry, EtagSingleItemCount)).ToArray()));
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal ReplicaMembershipSnapshot? Update(MembershipEntry entry, string etag, TableVersion tableVersion, int maximumRows = ReplicaMembershipProtocol.UnboundedRows)
    {
        const int ETagStep = 1;

        var previous = snapshot.Rows.FirstOrDefault(row => row.Address == entry.SiloAddress.ToParsableString());
        if (previous is null || etag != Version(previous.ETag) || !NextVersion(tableVersion))
        {
            return null;
        }

        // A status update based on an older read cannot move the heartbeat backwards.
        var updated = ReplicaMembershipRow.From(entry, checked(previous.ETag + ETagStep)) with
        { Alive = entry.IAmAliveTime > previous.Alive ? entry.IAmAliveTime : previous.Alive };
        var next = Next(new(tableVersion.Version, snapshot.Rows.Select(row => row.Address == previous.Address ? updated : row).ToArray()));
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal ReplicaMembershipSnapshot? Heartbeat(MembershipEntry entry, int maximumRows = ReplicaMembershipProtocol.UnboundedRows)
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

    internal ReplicaMembershipSnapshot Delete() => Next(new(checked(snapshot.Version + DeleteVersionStep), []));

    internal ReplicaMembershipSnapshot? Cleanup(DateTimeOffset beforeDate, int maximumRows = ReplicaMembershipProtocol.UnboundedRows)
    {
        const int VersionStep = 1;

        var retained = snapshot.Rows.Where(row => row.Status != SiloStatus.Dead || row.Alive >= beforeDate.UtcDateTime).ToArray();
        if (retained.Length == snapshot.Rows.Length)
        { return null; }
        var next = Next(new(checked(snapshot.Version + VersionStep), retained));
        next.ValidateCapacity(maximumRows);
        return next;
    }

    internal void ValidateCapacity(int maximumRows)
    {
        if (maximumRows <= ReplicaMembershipProtocol.UnboundedRows)
        { return; }
        if (snapshot.Rows.Length > maximumRows || Serialize().Length > membershipOptions.Value.MaximumSnapshotBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        foreach (var row in snapshot.Rows)
        {
            if (NativeSerialization.Measure(row) > membershipOptions.Value.MaximumRowBytes
                || row.Suspects.Length > membershipOptions.Value.MaximumSuspects
                || !ReplicaMembershipAuthorityValidation.Bounded(value: row.Address, membershipOptions: membershipOptions) || !ReplicaMembershipAuthorityValidation.Bounded(value: row.Host, membershipOptions: membershipOptions)
                || !ReplicaMembershipAuthorityValidation.Bounded(value: row.Name, membershipOptions: membershipOptions)
                || row.Suspects.Any(suspect => !ReplicaMembershipAuthorityValidation.Bounded(value: suspect.Address, membershipOptions: membershipOptions)))
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        }
    }

    private void CheckCanAdd(MembershipEntry entry, int maximumRows)
    {
        const int MaximumRowsValidationBoundary = 0;
        const string CheckCanAddEtagText = "0";

        if (maximumRows > MaximumRowsValidationBoundary && snapshot.Rows.Length >= maximumRows)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        if (maximumRows > MaximumRowsValidationBoundary)
        { ReplicaMembershipAuthorityValidation.Entry(entry: ReplicaMembershipAuthorityMapping.ToWire(entry, CheckCanAddEtagText), membershipOptions: membershipOptions); }
    }

    private bool NextVersion(TableVersion tableVersion) => snapshot.Version < long.MaxValue
        && tableVersion.Version == snapshot.Version + NextVersionVersionStep && tableVersion.VersionEtag == Version(snapshot.Version);

    private ReplicaMembershipSnapshot Next(ReplicaMembershipTableSnapshot updated) => new(ExpectedVersion, updated, membershipOptions);

    private static string Version(long value) => value.ToString(CultureInfo.InvariantCulture);
}
