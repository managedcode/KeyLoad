namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteDatabaseCompositionTokens
{
    internal const string ReadmePath = "README.md";
    internal const string ReadmeIntroductionStart = "# KeyLoad";
    internal const string ReadmeIntroductionEnd = "The cluster foundation";
    internal const string HtmlPath = "site/Features/BenchmarkComparisons/index.html";
    internal const string HeroStart = "<p class=\"hero-summary\">";
    internal const string ParagraphEnd = "</p>";
    internal const string HtmlCompositionStart = "<section class=\"anatomy\" id=\"composition\"";
    internal const string HtmlSectionEnd = "</section>";
    internal const string ReadmeCompositionStart = "## One database, connected models";
    internal const string ReadmeCompositionEnd = "## Development status";
    internal const string MissingRepository = "Composition content tests require the actual site repository path.";
    internal const string MissingSection = "The public database composition section is missing or incomplete.";
    internal const string OneDatabase = "one database for AI agents";
    internal const string CanonicalLinks = "canonical entity references";
    internal const string FamiliarSql = "SQL is the familiar shared language";
    internal const string QueueFlow = "Queue to knowledge graph";
    internal const string GraphFlow = "Graph to queued actions";
    internal const string LinkedEntities = "linked entities";
    internal const string GraphRelationships = "knowledge-graph relationships";
    internal const string QueuedActions = "enqueue actions";
    internal const string QueueMutation = "QueueToGraph";
    internal const string GraphMutation = "GraphToQueueMutation";
    internal const string CanonicalCall = "CALL keyload_documents_commit(@arguments)";
    internal const string SdkCommit = "CommitAsync";
    internal const string OfficialMcp = "official MCP";
    internal const string ProceduralStage = "initial bounded procedural composition stage";
    internal const string QualificationPending = "exact-source qualification pending";
    internal const string AtomicScope = "same atomic partition and transaction domain";
    internal const string LeaseBoundary = "does not lease or ACK";
    internal const string BlobsSameDatabase = "blobs remain part of the same database";
    internal const string BlobOperationsBoundary =
        "staged upload and publication use their existing operations outside this initial graph/queue batch";
    internal const string FullSqlPending = "Full declarative SQL";
    internal const string NativeClientPending = "native SQL-client protocol";
    internal const string CrossPartitionPending = "cross-partition composition";
    internal const string RequiredPending = "remain required and pending";
    internal const string FeaturePath = "docs/Features/DatabaseComposition.md";
    internal const string AdrPath = "docs/ADR/ADR-067-composable-agent-database.md";
    internal const string GitHubRoot = "https://github.com/managedcode/KeyLoad/blob/main/";
    internal const string HtmlTagPattern = "<[^>]*>";
    internal const string WordSeparator = " ";
    internal const int PatternTimeoutMilliseconds = 1000;

    internal static readonly string[] Models =
        ["documents", "typed tables", "graphs", "blobs", "queues", "events", "vectors/search", "time series"];

    internal static readonly string[] Flows =
        [QueueFlow, GraphFlow, LinkedEntities, GraphRelationships, QueuedActions, QueueMutation, GraphMutation];

    internal static readonly string[] StageContracts =
        [CanonicalCall, SdkCommit, OfficialMcp, ProceduralStage, QualificationPending, AtomicScope,
            LeaseBoundary, BlobsSameDatabase, BlobOperationsBoundary, FullSqlPending, NativeClientPending,
            CrossPartitionPending, RequiredPending];
}
