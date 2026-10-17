namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsNativeProtocol
{
    internal const string Collection = "analytics-native-source";
    internal const string Document = "original";
    internal const string Principal = "root";
    internal const string Json = "{\"secret\":\"analytics-private-document-canary\"}";
    internal const string PrivateDocument = "analytics-private-document-canary";
    internal const string PrivateVoter = "analytics-private-voter-canary";
    internal const string PrivateLeader = "analytics-private-leader-canary";
    internal const string DerivedDirectory = "derived-operational-analytics";
    internal const string RawDirectory = "events";
    internal const string SessionDirectory = "sessions";
    internal const string PutKind = "putDocument";
    internal const long ConfigureCut = 1;
    internal const long DocumentCut = 2;
    internal const long Revision = 1;
    internal const long OwnershipEpoch = 1;
    internal const long NoSnapshot = 0;
    internal const int ColdCycles = 2;
    internal const int MaximumRows = 512;
    internal const int MaximumReportBytes = 65_536;
    internal const int RejectedDays = 8;
    internal const string RangeParameter = "from";
    internal const string SnapshotParameter = "snapshot";
    internal const string RowSeparator = ":";
    internal const int MisalignedTicks = 1;
    internal const int NextHour = 1;
    internal const int LastCycleOffset = 1;
    internal const long InvalidPosition = -1;
    internal const long InexactPosition = 9_007_199_254_740_992;
    internal const int NoValue = 0;
    internal const long ArithmeticCommitted = 17;
    internal const long ArithmeticApplied = 12;
    internal const double Samples = 3;
    internal const double PositionSum = 20;
    internal const double AppliedSum = 15;
    internal const double BacklogSum = 5;
    internal const string OriginalCancellationObserved = "analytics-original-caller-cancel-observed";
    internal const string OriginalCompleted = "analytics-original-native-query-completed";
    internal const string WaitCancellationObserved = "analytics-caller-wait-canceled";
    internal const string Invalid = "The actual analytics fixture exceeded its complete native inventory or lost its owner.";
}
