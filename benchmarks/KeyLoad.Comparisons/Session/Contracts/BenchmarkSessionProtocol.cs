namespace KeyLoad.Comparisons;

internal static class BenchmarkSessionProtocol
{
    internal const string InvalidSessionSelection = "InvalidSessionSelection";
    internal const string InvalidSessionOptions = "InvalidSessionOptions";
    internal const string BenchmarkSessionClientDisposalFailed = "BenchmarkSessionClientDisposalFailed";
    internal const string BenchmarkSessionReadMismatch = "BenchmarkSessionReadMismatch";
    internal const string ConcurrentBenchmarkSessionTestsAreForbidden = "ConcurrentBenchmarkSessionTestsAreForbidden";
    internal const string InitialLoadAcknowledgementsIncomplete = "InitialLoadAcknowledgementsIncomplete";
    internal const string IngestionBatchOutsideClientShare = "IngestionBatchOutsideClientShare";
    internal const string InsufficientSessionClientsForMixedStreams = "InsufficientSessionClientsForMixedStreams";
    internal const string LiveSessionPrerequisiteFailedOrNotExecuted = "LiveSessionPrerequisiteFailedOrNotExecuted";
    internal const string MixedAcknowledgementsIncomplete = "MixedAcknowledgementsIncomplete";
    internal const string MixedStreamsDidNotOverlap = "MixedStreamsDidNotOverlap";
    internal const string AdapterSessionIndexLifecycleUnavailable = "AdapterSessionIndexLifecycleUnavailable";
    internal const string AdapterSessionQueryLifecycleUnavailable = "AdapterSessionQueryLifecycleUnavailable";
    internal const string SessionFinalCardinalityMismatch = "SessionFinalCardinalityMismatch";
    internal const string SessionLoadAlreadyAttempted = "SessionLoadAlreadyAttempted";
    internal const string SessionOperationAccountingIncomplete = "SessionOperationAccountingIncomplete";
    internal const string SessionQueryResultMismatch = "SessionQueryResultMismatch";
    internal const string SessionRequiresSingleOperationTiming = "SessionRequiresSingleOperationTiming";
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
