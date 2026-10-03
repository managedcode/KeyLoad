namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAliases
{
    internal const string Snapshot = "keyload.orleans.membership.snapshot.v1";
    internal const string Row = "keyload.orleans.membership.row.v1";
    internal const string Suspect = "keyload.orleans.membership.suspect.v1";
    internal const int VersionField = 0;
    internal const int RowsField = 1;
    internal const int AddressField = 0;
    internal const int TimeField = 1;
    internal const int StatusField = 1;
    internal const int ProxyPortField = 2;
    internal const int HostField = 3;
    internal const int NameField = 4;
    internal const int StartedField = 5;
    internal const int AliveField = 6;
    internal const int SuspectsField = 7;
    internal const int ETagField = 8;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaMembershipAliases.Snapshot)]
internal sealed record ReplicaMembershipTableSnapshot(
    [property: global::Orleans.Id(ReplicaMembershipAliases.VersionField)] long Version,
    [property: global::Orleans.Id(ReplicaMembershipAliases.RowsField)] ReplicaMembershipRow[] Rows);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaMembershipAliases.Suspect)]
internal sealed record ReplicaMembershipSuspect(
    [property: global::Orleans.Id(ReplicaMembershipAliases.AddressField)] string Address,
    [property: global::Orleans.Id(ReplicaMembershipAliases.TimeField)] DateTime Time);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaMembershipAliases.Row)]
internal sealed record ReplicaMembershipRow(
    [property: global::Orleans.Id(ReplicaMembershipAliases.AddressField)] string Address,
    [property: global::Orleans.Id(ReplicaMembershipAliases.StatusField)] SiloStatus Status,
    [property: global::Orleans.Id(ReplicaMembershipAliases.ProxyPortField)] int ProxyPort,
    [property: global::Orleans.Id(ReplicaMembershipAliases.HostField)] string Host,
    [property: global::Orleans.Id(ReplicaMembershipAliases.NameField)] string Name,
    [property: global::Orleans.Id(ReplicaMembershipAliases.StartedField)] DateTime Started,
    [property: global::Orleans.Id(ReplicaMembershipAliases.AliveField)] DateTime Alive,
    [property: global::Orleans.Id(ReplicaMembershipAliases.SuspectsField)] ReplicaMembershipSuspect[] Suspects,
    [property: global::Orleans.Id(ReplicaMembershipAliases.ETagField)] long ETag)
{
    /// <summary>Copies native fields without dropping suspicion or heartbeat metadata.</summary>
    public static ReplicaMembershipRow From(MembershipEntry entry, long etag) => new(entry.SiloAddress.ToParsableString(), entry.Status,
        entry.ProxyPort, entry.HostName, entry.SiloName, entry.StartTime, entry.IAmAliveTime,
        (entry.SuspectTimes ?? []).Select(pair => new ReplicaMembershipSuspect(pair.Item1.ToParsableString(), pair.Item2)).ToArray(), etag);

    /// <summary>Reconstructs the exact native row.</summary>
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
