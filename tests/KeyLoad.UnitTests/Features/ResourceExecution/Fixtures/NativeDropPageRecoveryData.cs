namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageRecoveryData
{
    internal static readonly TimeSpan AttemptLifetime = TimeSpan.FromSeconds(5);
    internal const string ExpectedOriginalFailuresKey = "KeyLoad.NativeDrop.ExpectedOriginalFailures";
    internal const int ExpectedOriginalFailureCount = 1;
    internal const long TinyReadBytes = 1;
    internal const int TinyReadRecords = 1;
    internal const string OtherTable = "native_drop_control";
    internal const string OtherRow = "control-1";
    internal const string OtherJson = "{\"id\":\"control-1\",\"value\":\"unrelated\"}";
    internal const string MissingFailure = "The native cleanup page unexpectedly succeeded.";
}
