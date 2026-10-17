namespace KeyLoad.Server;

internal static class OperationalAnalyticsProtocol
{
    internal const string Section = "KeyLoad:OperationalAnalytics";
    internal const string DirectoryName = "derived-operational-analytics";
    internal const string Site = "keyload-node";
    internal const string Sample = "replica-state-sample";
    internal const string Committed = "committed_position";
    internal const string Applied = "applied_position";
    internal const string Backlog = "committed_backlog";
    internal const string Busy = "The private analytics operation owner is busy.";
    internal const string InvalidPolicy = "The private operational analytics policy is invalid.";
    internal const string InvalidReport = "The private analytics report exceeds its fixed contract.";
    internal const long MaximumExactInteger = 9_007_199_254_740_991;
    internal const int WorkerCount = 1;
    internal const int ValueCount = 3;
    internal const int MetricCount = 4;
    internal const int DimensionCount = 0;
    internal const int MinimumValueCharacters = 16;
    internal const int MaximumReportBytes = 65_536;
}
