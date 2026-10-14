namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeMembershipProviderRow(
    SiloAddress SiloAddress,
    string SiloName,
    string HostName,
    string? RoleName,
    int ProxyPort,
    int FaultZone,
    int UpdateZone,
    DateTime StartTime,
    SiloStatus Status,
    DateTime IAmAliveTime,
    Tuple<SiloAddress, DateTime>[]? SuspectTimes,
    string RowEtag);

internal sealed record NativeMembershipProviderObservation(
    NativeMembershipProviderRow[] Rows,
    int TableVersion,
    string TableVersionEtag);
