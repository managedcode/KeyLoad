namespace KeyLoad.Core.Messaging;

internal static class DueWorkFields
{
    internal const int Kind = 0;
    internal const int Lane = 1;
    internal const int Id = 2;
    internal const int Creator = 3;
    internal const int Revision = 4;
    internal const int Generation = 5;
    internal const int Ordinal = 6;
    internal const int DueAt = 7;
    internal const string UtcZone = "UTC";
    internal const string GuidFormat = "N";
    internal const long MinimumIntervalTicks = TimeSpan.TicksPerSecond;
    internal const long MaximumIntervalTicks = 365L * TimeSpan.TicksPerDay;
}
