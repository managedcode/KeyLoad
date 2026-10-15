namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyRf3Protocol
{
    internal const string TenantPrefix = "graph-path-policy-";
    internal const string Database = "path-database";
    internal const string Domain = "path-domain";
    internal const string Documents = "path-documents";
    internal const string Projects = "path-projects";
    internal const string Graph = "path-graph";
    internal const string Root = "root";
    internal const string Middle = "middle";
    internal const string Private = "private";
    internal const string Direct = "direct";
    internal const string Target = "target";
    internal const string RootJson = "{\"name\":\"root\"}";
    internal const string MiddleJson = "{\"name\":\"middle\"}";
    internal const string PrivateJson = "{\"name\":\"private-path-canary\"}";
    internal const string DirectJson = "{\"name\":\"direct\"}";
    internal const string TargetJson = "{\"name\":\"target\"}";
    internal const string Owner = "path-reader-owner";
    internal const string ForeignOwner = "path-foreign-owner";
    internal const string RootDirect = "root-direct";
    internal const string RootMiddle = "root-middle";
    internal const string MiddleTarget = "middle-target";
    internal const string Label = "route";
    internal const string GenericFailure = "The database operation could not be completed.";
    internal const string MissingVertex = "The graph vertex is unavailable.";
    internal const int Version = 1;
    internal const int Depth = 2;
    internal const int Vertices = 5;
    internal const int Edges = 3;
    internal const int Results = 5;
    internal const int Fusion = 60;
    internal const int ColdCycles = 2;
    internal const long SeedMutations = 8L;
    internal const long FinalMutations = 9L;
}
