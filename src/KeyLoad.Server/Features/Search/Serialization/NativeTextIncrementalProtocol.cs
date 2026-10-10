namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalProtocol
{
    internal const int FormatVersion = 1;
    internal const int ManifestFormatVersion = 2;
    internal const string RootDirectory = "text-projections";
    internal const string EnrollmentFile = "generation.bin";
    internal const string ManifestFile = "incremental.bin";
    internal const string PendingManifestFile = "incremental.pending";
    internal const string IntentFile = "intent.bin";
    internal const string PendingIntentFile = "intent.pending";
    internal const long InitialSequence = 0;
    internal const long InitialGeneration = 1;
    internal const ulong InitialRecord = 1;
}
