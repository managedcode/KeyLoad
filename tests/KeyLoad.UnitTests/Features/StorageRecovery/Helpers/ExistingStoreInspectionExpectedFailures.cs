namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ExistingStoreInspectionExpectedFailures
{
    internal const string Argument = "System.ArgumentException";
    internal const string ArgumentNull = "System.ArgumentNullException";
    internal const string ArgumentOutOfRange = "System.ArgumentOutOfRangeException";
    internal const string DatabaseNotFound = "ZoneTree.Exceptions.DatabaseNotFoundException";
    internal const string DirectoryNotFound = "System.IO.DirectoryNotFoundException";
    internal const string FileNotFound = "System.IO.FileNotFoundException";
    internal const string Io = "System.IO.IOException";
    internal const string KeyLoad = "KeyLoad.KeyLoadException";
    internal const string Corruption = "Corruption";
    internal const string FormatUnsupported = "FormatUnsupported";
    internal const string TokenInvalidated = "TokenInvalidated";
}
