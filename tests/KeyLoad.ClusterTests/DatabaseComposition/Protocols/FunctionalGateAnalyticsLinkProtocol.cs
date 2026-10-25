namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsLinkProtocol
{
    internal const int TableIndex = 2;
    internal const int StoreIndex = 2;
    internal const int Absent = 0;
    internal const int Initial = 1;
    internal const int Updated = 2;
    internal const int GenerationWrites = 3;
    internal const double Value = 4;
    internal const string Prefix = "gate_link_lifetime_";
    internal static readonly DateTimeOffset From = DateTimeOffset.UnixEpoch;
    internal static readonly DateTimeOffset Until = DateTimeOffset.UnixEpoch.AddHours(Initial);
}
