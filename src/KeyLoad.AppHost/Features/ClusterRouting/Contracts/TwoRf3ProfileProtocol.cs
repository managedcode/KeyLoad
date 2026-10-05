namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3ProfileProtocol
{
    internal const string Section = "KeyLoadTests:ClusterRouting";
    internal const string Setting = Section + ":Profile";
    internal const string Profile = "two-rf3";
    internal const string Invalid = "The two-RF3 membership profile is invalid.";
    internal const string EphemeralSetting = "KeyLoad:Ephemeral";
    internal const int MembersPerGroup = 3;
    internal const int TotalNodes = 6;
    internal const int HttpPort = 8080;
    internal const int SiloPort = 11111;
}
