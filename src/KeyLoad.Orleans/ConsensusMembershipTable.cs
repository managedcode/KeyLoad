using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage;
using Orleans.Runtime;

namespace KeyLoad.Orleans;

/// <summary>Uses the already running local consensus endpoint; no membership grain is involved in bootstrap.</summary>
public sealed class ConsensusMembershipTable(DatabaseEngine database, ICommitCoordinator coordinator, string clusterId,
    string internalPrincipal) : IMembershipTable
{
    private const string Key = "orleans-membership";
    private static string Version(long value) => value.ToString(CultureInfo.InvariantCulture);
    private async Task<(MembershipRecord? Record, TableSnapshot Snapshot)> Read()
    {
        await coordinator.ReadBarrierAsync();
        var record = database.Store.Read(view => view.GetRecord<MembershipRecord>(KeyCodec.Encode("membership", Key)));
        return (record, record is null ? new(0, []) : JsonDefaults.Deserialize<TableSnapshot>(System.Text.Encoding.UTF8.GetBytes(record.Json)));
    }
    private async Task<bool> CompareExchange(MembershipRecord? old, TableSnapshot snapshot)
    {
        var command = new MembershipMutation(Key, old?.Version ?? 0, System.Text.Json.JsonSerializer.Serialize(snapshot, JsonDefaults.Options));
        return (await coordinator.SubmitAsync(OperationKind.Membership, Guid.NewGuid(), internalPrincipal,
            System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))).Get<bool>();
    }
    public async Task InitializeMembershipTable(bool tryInitTableVersion) => await Read();
    public async Task<MembershipTableData> ReadAll()
    {
        var (_, snapshot) = await Read();
        return Data(snapshot, snapshot.Rows);
    }
    public async Task<MembershipTableData> ReadRow(SiloAddress key)
    {
        var (_, snapshot) = await Read();
        return Data(snapshot, snapshot.Rows.Where(r => r.Address == key.ToParsableString()).ToArray());
    }
    private static MembershipTableData Data(TableSnapshot snapshot, Row[] rows) => new(rows.Select(row =>
        Tuple.Create(row.ToEntry(), Version(row.ETag))).ToList(), new TableVersion(checked((int)snapshot.Version), Version(snapshot.Version)));
    public async Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
    {
        var (record, snapshot) = await Read();
        if (tableVersion.Version != snapshot.Version + 1 || tableVersion.VersionEtag != Version(snapshot.Version)
            || snapshot.Rows.Any(r => r.Address == entry.SiloAddress.ToParsableString())) return false;
        return await CompareExchange(record, new(tableVersion.Version, snapshot.Rows.Append(Row.From(entry, 1)).ToArray()));
    }
    public async Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        var (record, snapshot) = await Read();
        var old = snapshot.Rows.FirstOrDefault(r => r.Address == entry.SiloAddress.ToParsableString());
        if (old is null || etag != Version(old.ETag) || tableVersion.Version != snapshot.Version + 1
            || tableVersion.VersionEtag != Version(snapshot.Version)) return false;
        // A stale membership update must not overwrite a newer heartbeat.
        var updated = Row.From(entry, old.ETag + 1) with { Alive = entry.IAmAliveTime > old.Alive ? entry.IAmAliveTime : old.Alive };
        return await CompareExchange(record, new(tableVersion.Version, snapshot.Rows.Select(r => r.Address == old.Address ? updated : r).ToArray()));
    }
    public async Task UpdateIAmAlive(MembershipEntry entry)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var (record, snapshot) = await Read();
            var address = entry.SiloAddress.ToParsableString();
            var old = snapshot.Rows.FirstOrDefault(r => r.Address == address);
            if (old is null || old.Alive >= entry.IAmAliveTime) return;
            var updated = snapshot with { Rows = snapshot.Rows.Select(r => r.Address == address ? r with { Alive = entry.IAmAliveTime } : r).ToArray() };
            if (await CompareExchange(record, updated)) return;
        }
        throw Errors.Fail(ErrorCode.ResourceExhausted, "Membership heartbeat contention exceeded its retry budget.");
    }
    public async Task DeleteMembershipTableEntries(string requestedCluster)
    {
        if (requestedCluster != clusterId) throw Errors.Fail(ErrorCode.PermissionDenied, "The cluster ID does not match.");
        var (record, snapshot) = await Read();
        if (!await CompareExchange(record, new(snapshot.Version + 1, []))) throw Errors.Fail(ErrorCode.Conflict, "Membership changed during deletion.");
    }
    public async Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
    {
        var (record, snapshot) = await Read();
        var retained = snapshot.Rows.Where(r => r.Status != SiloStatus.Dead || r.Alive >= beforeDate.UtcDateTime).ToArray();
        if (retained.Length != snapshot.Rows.Length) await CompareExchange(record, new(snapshot.Version + 1, retained));
    }
    private sealed record TableSnapshot(long Version, Row[] Rows);
    private sealed record Suspect(string Address, DateTime Time);
    private sealed record Row(string Address, SiloStatus Status, int ProxyPort, string Host, string Name,
        DateTime Started, DateTime Alive, Suspect[] Suspects, long ETag)
    {
        public static Row From(MembershipEntry entry, long etag) => new(entry.SiloAddress.ToParsableString(), entry.Status,
            entry.ProxyPort, entry.HostName, entry.SiloName, entry.StartTime, entry.IAmAliveTime,
            (entry.SuspectTimes ?? []).Select(pair => new Suspect(pair.Item1.ToParsableString(), pair.Item2)).ToArray(), etag);
        public MembershipEntry ToEntry() => new()
        {
            SiloAddress = SiloAddress.FromParsableString(Address), Status = Status, ProxyPort = ProxyPort,
            HostName = Host, SiloName = Name, StartTime = Started, IAmAliveTime = Alive,
            SuspectTimes = Suspects.Select(s => Tuple.Create(SiloAddress.FromParsableString(s.Address), s.Time)).ToList()
        };
    }
}
