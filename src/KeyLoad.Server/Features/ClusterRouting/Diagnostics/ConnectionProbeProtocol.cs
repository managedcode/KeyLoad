namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ConnectionProbeProtocol
{
    internal const string Prefix = "connection-";
    internal const string Active = "active";
    internal const string Closed = "closed";
    internal const string Separator = "-";
    internal const int NameParts = 3;
    internal const int ArmPart = 0;
    internal const int RequestPart = 1;
    internal const int StatePart = 2;
    internal const int Absent = 0;
    internal const int One = 1;
    internal const long ManagementGrainKey = 0;
    internal const string Invalid = "The private connection activation evidence is invalid.";
}
