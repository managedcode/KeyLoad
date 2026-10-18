namespace KeyLoad.Comparisons;

internal static class BenchmarkSessionProtocol
{
    internal const string InvalidSessionSelection = "InvalidSessionSelection";
    internal const string InvalidSessionOptions = "InvalidSessionOptions";
    internal const string BenchmarkSessionClientDisposalFailed = "BenchmarkSessionClientDisposalFailed";
    internal const string BenchmarkSessionReadMismatch = "BenchmarkSessionReadMismatch";
    internal const string ConcurrentBenchmarkSessionTestsAreForbidden = "ConcurrentBenchmarkSessionTestsAreForbidden";
    internal const string InitialLoadAcknowledgementsIncomplete = "InitialLoadAcknowledgementsIncomplete";
    internal const string InsufficientSessionClientsForMixedStreams = "InsufficientSessionClientsForMixedStreams";
    internal const string LiveSessionPrerequisiteFailedOrNotExecuted = "LiveSessionPrerequisiteFailedOrNotExecuted";
    internal const string MixedNativeAcknowledgementsIncomplete = "MixedNativeAcknowledgementsIncomplete";
    internal const string MixedStreamsDidNotOverlap = "MixedStreamsDidNotOverlap";
    internal const string NativeAdapterSessionIndexLifecycleUnavailable = "NativeAdapterSessionIndexLifecycleUnavailable";
    internal const string NativeAdapterSessionQueryLifecycleUnavailable = "NativeAdapterSessionQueryLifecycleUnavailable";
    internal const string SessionFinalCardinalityMismatch = "SessionFinalCardinalityMismatch";
    internal const string SessionLoadAlreadyAttempted = "SessionLoadAlreadyAttempted";
    internal const string SessionNativeOperationAccountingIncomplete = "SessionNativeOperationAccountingIncomplete";
    internal const string SessionNativeQueryResultMismatch = "SessionNativeQueryResultMismatch";
    internal const string SessionRequiresSingleNativeOperationTiming = "SessionRequiresSingleNativeOperationTiming";
    internal const string Create = "create";
    internal const string Delete = "delete";
    internal const string Failed = "failed";
    internal const string Index = "index";
    internal const string LoadMillion = "load-million";
    internal const string Measured = "measured";
    internal const string Mixed = "mixed";
    internal const string RandomRead = "random-read";
    internal const string Read = "read";
    internal const string Search = "search";
    internal const string SequentialRead = "sequential-read";
    internal const string Unavailable = "unavailable";
    internal const string Update = "update";
    internal const string VerifyFinal = "verify-final";
}
