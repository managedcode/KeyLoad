using System.Net;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessConstants
{
    public const int SmallDocuments = 16;
    public const int SmallOperations = 12;
    public const int SmallWarmup = 2;
    public const int SmallRepetitions = 2;
    public const int SmallConcurrency = 2;
    public const int SmallPayloadBytes = 128;
    public const int SmallDimensions = 8;
    public const int SmallTopK = 3;
    public const int AcceptedStatusCode = 202;
    public const int QueryErrorStatusCode = 400;
    public const int MaximumNativeCodeLength = 256;
    public const int SupportedScenarioCount = 6;
    public const int UnsupportedScenarioCount = 4;
    public const int SuccessfulCaseCount = 12;
    public const int TotalCaseCount = 20;
    public const int WriteDocumentCount = 28;
    public const int TotalNeo4jDocumentCount = 72;
    public const int CsvLineCount = 145;
    public const int MismatchCsvLineCount = 73;
    public const string MeasuredStatus = "measured";
    public const string FailedStatus = "failed";
    public const string UnsupportedStatus = "unsupported";
    public const string SetupPrefix = "setup:";
    public const string Neo4jUsername = "neo4j";
    public const string Neo4jTargetName = "Neo4j";
    public const string PointReadProgress = "Neo4j: PointRead,";
    public const string DocumentWriteProgress = "Neo4j: DocumentWrite,";
    public const string DocumentLabelPrefix = "Benchmark_";
    public const string QueryPath = "db/neo4j/query/v2";
    public const string MutatedJson = "{}";
    public const string DatabaseName = "neo4j";
    public const string StatementProperty = "statement";
    public const string ParametersProperty = "parameters";
    public const string MaxExecutionTimeProperty = "maxExecutionTime";
    public const string DataProperty = "data";
    public const string ErrorsProperty = "errors";
    public const string ErrorCodeProperty = "code";
    public const string ErrorMessageProperty = "message";
    public const string FieldsProperty = "fields";
    public const string ValuesProperty = "values";
    public const string QueryTypeProperty = "queryType";
    public const string BookmarksProperty = "bookmarks";
    public const string NameProperty = "name";
    public const string IdProperty = "id";
    public const string JsonProperty = "json";
    public const string IdParameter = "$id";
    public const string NameParameter = "$name";
    public const string IdsParameter = "$ids";
    public const string DocumentsParameter = "$documents";
    public const string Neo4jPrefix = "Neo4j:";
    public const string InvalidQueryResponse = "Neo4j:InvalidQueryResponse";
    public const string FixtureSetupFailure = "Neo4jHarnessFixtureSetupFailed";
    public const string FixtureCleanupFailure = "Neo4jHarnessFixtureCleanupFailed";
    public const string RegressionFailure = "Neo4jHarnessRegressionFailed";
    public const string TargetCleanupFailure = "Neo4jHarnessTargetCleanupFailed";
    public const string RunnerSetupFailure = "Neo4jHarnessSetupFailure";
    public const string CapturedFailure = "Neo4jHarnessCapturedFailure";
    public const string ReportCleanupFailure = "Neo4jHarnessReportCleanupFailed";
    public const string ReportDirectoryPrefix = "keyload-neo4j-harness-";
    public const string ResultsJsonFile = "results.json";
    public const string ResultsMarkdownFile = "results.md";
    public const string SamplesCsvFile = "samples.csv";
    public const string NativeCodePrefix = "Neo";
    public const string NativeCodeClientError = "ClientError";
    public const string NativeCodeSchema = "Schema";
    public const string NativeCodeGeneral = "General";
    public const string NativeCodeUnknownError = "UnknownError";
    public const string InvalidCodeComponent = "Bad-Code";
    public const string NonAsciiCodeComponent = "Schéma";
    public const string NumericCodeComponent = "1ClientError";
    public const string UnderscoreCodeComponent = "_ClientError";
    public const string NativeTestMessage = "controlled native message";
    public const string DuplicateHttpDiagnosticPrefix = "Neo4jHarnessDuplicateHTTP:";
    public const string NativeCodeDiagnosticSeparator = ";code:";
    public const string MarkerParameter = "id";
    public const string ConstraintParameter = "name";
    public const string IdsParameterName = "ids";
    public const string DocumentsParameterName = "documents";

    public static ComparisonOptions SmallOptions() => CreateOptions(SmallWarmup, SmallRepetitions);

    public static ComparisonOptions SetupFailureOptions() => CreateOptions(0, 1);

    public static int[] UnexpectedQueryStatuses() =>
    [
        (int)HttpStatusCode.OK,
        (int)HttpStatusCode.Created,
        (int)HttpStatusCode.NoContent,
        (int)HttpStatusCode.Unauthorized,
        (int)HttpStatusCode.Forbidden,
        (int)HttpStatusCode.NotFound,
        (int)HttpStatusCode.InternalServerError,
        (int)HttpStatusCode.ServiceUnavailable
    ];

    public static Scenario[] ExpectedScenarios() =>
    [
        Scenario.PointRead,
        Scenario.DocumentWrite,
        Scenario.VectorExact,
        Scenario.QueueCycle,
        Scenario.GraphNeighbors,
        Scenario.GraphTraverse,
        Scenario.StreamAppend,
        Scenario.StreamRead,
        Scenario.DocumentUpdate,
        Scenario.DocumentDelete
    ];

    public static bool IsSupported(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite
        or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.DocumentUpdate or Scenario.DocumentDelete;

    private static ComparisonOptions CreateOptions(int warmup, int repetitions) => new()
    {
        Documents = SmallDocuments,
        Operations = SmallOperations,
        Warmup = warmup,
        Repetitions = repetitions,
        Concurrency = SmallConcurrency,
        PayloadBytes = SmallPayloadBytes,
        Dimensions = SmallDimensions,
        TopK = SmallTopK,
    };
}
