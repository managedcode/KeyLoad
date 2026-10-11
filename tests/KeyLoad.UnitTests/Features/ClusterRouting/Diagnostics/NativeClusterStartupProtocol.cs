namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeClusterStartupProtocol
{
    internal const int SchemaVersion = 1;
    internal const int MaximumEvents = 64;
    internal const int MaximumFileBytes = 1_048_576;
    internal const int MaximumOutputBytes = 65_536;
    internal const int BufferBytes = 4096;
    internal const int MaximumLineBytes = 1024;
    internal const int MaximumIdentifierCharacters = 256;
    internal const int MaximumExceptionDepth = 8;
    internal const string DirectoryName = "native-cluster-startup";
    internal const string CleanupJoined = "cleanupJoined";
    internal const string CallerCancelled = "callerCancelled";
    internal const string DeadlineCancelled = "deadlineCancelled";
    internal const string Files = "files";
    internal const string ExceptionTypes = "exceptionTypes";
    internal const string Kind = "NativeTestClusterStartupFailure";

    internal static bool Identifier(string value)
        => value.Length is > 0 and <= MaximumIdentifierCharacters
            && value.All(static character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-' or '`');
}
