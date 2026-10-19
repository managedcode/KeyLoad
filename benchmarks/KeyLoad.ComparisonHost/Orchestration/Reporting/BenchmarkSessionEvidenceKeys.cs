namespace KeyLoad.ComparisonHost.Features.BenchmarkOrchestration;

/// <summary>Named structural fields of the original session envelope and its explicit execution startup inputs.</summary>
internal static class BenchmarkSessionEvidenceKeys
{
    internal const string SchemaVersion = "schemaVersion";
    internal const string SessionId = "sessionId";
    internal const string Target = "target";
    internal const string NodeCount = "nodeCount";
    internal const string SourceRevision = "sourceRevision";
    internal const string RunId = "runId";
    internal const string Attempt = "attempt";
    internal const string JobId = "jobId";
    internal const string Repository = "repository";
    internal const string Reference = "ref";
    internal const string Workflow = "workflow";
    internal const string Cases = "cases";
    internal const string OuterRuntime = "outerRuntime";
    internal const string RuntimeReport = "runtimeReport";
    internal const string QualificationReport = "qualificationReport";
    internal const string ResourceReport = "resourceReport";
    internal const string Outcome = "outcome";
    internal const string Reason = "reason";
    internal const string Path = "path";
    internal const string Sha256 = "sha256";
    internal const string Id = "id";
    internal const string Status = "status";
    internal const string VerifiedRecords = "verifiedRecords";
    internal const string Lanes = "lanes";
    internal const string Detail = "detail";
    internal const string RuntimeTestName = "testName";
    internal const string ResultElement = "UnitTestResult";
    internal const string SessionEnvironment = "Benchmarks__SessionId";
    internal const string ProfileEnvironment = "Benchmarks__EvidenceProfile";
    internal const string SourceEnvironment = "Benchmarks__SourceRevision";
    internal const string AdminEnvironment = "Benchmarks__AdminKey";
    internal const string OutputEnvironment = "Benchmarks__Output";
    internal const string StorageEnvironment = "Benchmarks__Storage";
    internal const string RunEnvironment = "GITHUB_RUN_ID";
    internal const string AttemptEnvironment = "GITHUB_RUN_ATTEMPT";
    internal const string RepositoryEnvironment = "GITHUB_REPOSITORY";
    internal const string ReferenceEnvironment = "GITHUB_REF";
    internal const string WorkflowEnvironment = "GITHUB_WORKFLOW";
    internal const string ShaEnvironment = "GITHUB_SHA";
    internal const string JobEnvironment = "KEYLOAD_COMPARISON_JOB_ID";
    internal const string FirstEndpointEnvironment = "Benchmarks__Execution__Endpoints__0";
    internal const string OperationsEnvironment = "Benchmarks__Operations";
    internal const string WarmupEnvironment = "Benchmarks__Warmup";
    internal const string ApiKeyEnvironment = "Benchmarks__Execution__ApiKey";
}
