namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3ProfileProtocol
{
    internal const string Section = "KeyLoadTests:ClusterRouting";
    internal const string Setting = Section + ":Profile";
    internal const string ProtectedDocumentMovementSetting = Section + ":ProtectedDocumentMovement";
    internal const string MovementMaxBatchBytesSetting = Section + ":MovementMaxBatchBytes";
    internal const string MovementMaxFrameBytesSetting = Section + ":MovementMaxFrameBytes";
    internal const string MovementMaxFrameBytesEnvironment = "KeyLoad__StorageExecution__MaxFrameBytes";
    internal const string MovementMaxBatchBytesEnvironment = "KeyLoad__DatabaseLimits__MaxBatchBytes";
    internal const string MovementEnvironment = "KeyLoad__PartitionMovementExecution__Enabled";
    internal const string RemoteQuerySetting = Section + ":RemotePartitionQueries";
    internal const string RemoteQueryEnvironment = "KeyLoad__MembershipAuthority__RemotePartitionQueries";
    internal const string RemoteDocumentSetting = Section + ":RemoteDocumentReads";
    internal const string RemoteDocumentEnvironment = "KeyLoad__MembershipAuthority__RemoteDocumentReads";
    internal const string RegistrationSetting = Section + ":RegisterPhysicalOwners";
    internal const string RegistrationEnvironment = "KeyLoad__MembershipAuthority__RegisterPhysicalOwners";
    internal const string Enabled = "true";
    internal const string Disabled = "false";
    internal const string Profile = "two-rf3";
    internal const string Invalid = "The two-RF3 membership profile is invalid.";
    internal const string EphemeralSetting = "KeyLoad:Ephemeral";
    internal const string BenchmarksEnabledSetting = "Benchmarks:Enabled";
    internal const int MembersPerGroup = 3;
    internal const int TotalNodes = 6;
    internal const int HttpPort = 8080;
    internal const int SiloPort = 11111;
}
